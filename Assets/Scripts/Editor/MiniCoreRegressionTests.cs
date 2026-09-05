using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using LivingCity.Personnel;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LivingCity.Tests
{
    /// <summary>Reproductions of the MiniCore play-session failures, using the shared
    /// runtime services. Run in Edit mode so no campaign or active round is disturbed.</summary>
    public static partial class MiniCoreRegressionTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        [MenuItem("Tools/Tests/Mini Core Regressions")]
        static void Menu()
        {
            var failures = Run();
            if (failures.Count == 0) Debug.Log("MiniCore regressions: PASS (94 scenarios)");
            else Debug.LogError(string.Join("\n", failures));
        }

        public static List<string> Run()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Run MiniCore regressions in Edit mode.");
            using var navigation = new NavigationScope();
            var failures = new List<string>();
            Check(failures, "orphan doorway", OrphanDoorway);
            Check(failures, "late escort", LateEscort);
            Check(failures, "hidden suspect", HiddenSuspect);
            Check(failures, "resolved swarm", ResolvedSwarm);
            Check(failures, "streamed furniture", StreamedFurniture);
            Check(failures, "retired walker tick", RetiredWalkerTick);
            Check(failures, "response parking destination", ResponseParkingDestination);
            Check(failures, "shoulder blocks pull-out", ShoulderBlocksPullOut);
            Check(failures, "custody owns its car", CustodyOwnsCar);
            Check(failures, "subway blocks side shop", SubwayBlocksSideShop);
            Check(failures, "stationary passing reservation", StationaryPassingReservation);
            Check(failures, "simultaneous complaint arrests", SimultaneousComplaintArrests);
            Check(failures, "stalled foot response", StalledFootResponse);
            Check(failures, "police question approaches a clear spot beside street furniture", BlockedPoliceChallengePoint);
            Check(failures, "police question routes around broad street furniture", BroadPoliceChallengePoint);
            Check(failures, "moving prisoner transfers retain bounded progress deadlines", MovingTransferKeepsItsDeadline);
            Check(failures, "block cards only show our own collector rounds", BlockRoundBelongsToPlayer);
            Check(failures, "turning arc rejects a parked corner", TurningArcRejectsParkedCorner);
            Check(failures, "blocked turn retains physical progress and reverses out", BlockedTurningArcKeepsPhysicalProgress);
            Check(failures, "turn finishes after its rear axle clears the arc", TurningArcFinishesWithoutYawSnap);
            Check(failures, "settled parking beside a motorcycle completes", SettledParkingBesideMotorcycle);
            Check(failures, "blocked reverse does not seal a clear junction", BlockedReverseDoesNotSealClearJunction);
            Check(failures, "destroyed car collision ghost", DestroyedCarCollisionGhost);
            Check(failures, "destroyed combat observer", DestroyedCombatObserver);
            Check(failures, "rival arrives at court", RivalArrivesAtCourt);
            Check(failures, "short custody route around parked props", ShortCustodyRoute);
            Check(failures, "hidden lieutenant is not an arrest target", HiddenArrestTarget);
            Check(failures, "recognition recovers a loaded open docket", RecognitionRetainsLoadedDocket);
            Check(failures, "destroyed member during AI march", DestroyedMemberDuringMarch);
            Check(failures, "unreachable route retried after geometry changes", FailedRouteChanges);
            Check(failures, "parking admission before road approach", ParkingAdmission);
            Check(failures, "parking motion respects physical bodies", ParkingMotionBody);
            Check(failures, "parking exit clears neighbouring stall", ParkingExitNeighbour);
            Check(failures, "destination ordered while crossing", DestinationWhileCrossing);
            Check(failures, "parking catalogue fits generated bays", ParkingCatalogueFits);
            Check(failures, "parking yaw and translation clear together", ParkingYawWithTranslation);
            Check(failures, "interrupted station approach resumes", InterruptedStationApproach);
            Check(failures, "steered parking arrival and departure", SteeredParkingManeuvers);
            Check(failures, "retired custody member loses command reference", RetiredCustodyMember);
            Check(failures, "parking departures cannot starve", ParkingDepartureFairness);
            Check(failures, "custody walks survive crew cohesion", CustodyDoesNotTether);
            Check(failures, "off-centre junction entry is continuous", OffCentreJunctionEntry);
            Check(failures, "bag returns to the actual headquarters approach", BagReachesHeadquarters);
            Check(failures, "walk from either door around a parked sedan", WalkAroundParkedSedan);
            Check(failures, "short street braking after response ends", ShortStreetResponseBraking);
            Check(failures, "sidestep does not cut a pole corner", SidestepClearsWholeSegment);
            Check(failures, "accelerated pedestrian keeps simulated pace", AcceleratedWalkingPace);
            Check(failures, "flight continues after its first leg", FlightContinues);
            Check(failures, "flight seeks cover and turns at the city edge", FlightSeeksCover);
            Check(failures, "detoured stride cannot snap through a chair", DetouredCorner);
            Check(failures, "entry crossing is not completed indoor custody", EntryIsNotInside);
            Check(failures, "ordered flight uses the sprint gait", OrderedFlightSprints);
            Check(failures, "crew answers police reinforcements after first patrol dies", PoliceReinforcements);
            Check(failures, "graph recovery stays on the same side of a pole", GraphRecoveryPole);
            Check(failures, "AI does not send a jailed lieutenant's remaining hoods", JailedLeaderCannotMarch);
            Check(failures, "parked patrol does not forecast traffic at a parking exit", ParkedPatrolAtGate);
            Check(failures, "recruit joins an indoor lieutenant from the pavement", RecruitBesideIndoorLeader);
            Check(failures, "crew spawn rejects a sidewalk seat inside a building", BlockedGraphSpawn);
            Check(failures, "civilian initialization chooses a clear connected graph seat", CivilianGraphSpawn);
            Check(failures, "automatic cadence survives a coarse simulation step", AutomaticCadence);
            Check(failures, "blocked gun does not accumulate future rounds", BlockedGunCadence);
            Check(failures, "opponents fire in time order and surrender cancels pending rounds", InterleavedRounds);
            Check(failures, "recycled prop footprint follows its fitted scale", PooledPropScale);
            Check(failures, "carried rubbish does not become a permanent street obstacle", CarriedPropIsNotStatic);
            Check(failures, "parked glow does not enlarge a vehicle's walking footprint", ParkedGlowFootprint);
            Check(failures, "streamed parked glow releases markers and unlit registrations", ParkedGlowRecycling);
            Check(failures, "clear travel beside a pole is not relocated by crew recovery", UnwedgePreservesTravel);
            Check(failures, "crew recovery keeps a clear chord and refuses a solid interior", UnwedgeConnectedRecovery);
            Check(failures, "junction waiting priority survives staggered car updates", StableJunctionWaitingPriority);
            Check(failures, "shared shelter door admits and releases every visitor", SharedShelterPassage);
            Check(failures, "broken shop windows do not lock the shared entrance", SmashedShelterPassage);
            Check(failures, "player retask supersedes a finished job's return home", RetaskFinishedJob);
            Check(failures, "conviction after bail preserves the body and its pickup door", BailedConvictionBody);
            Check(failures, "held hood survives the former crew root being retired", OrphanedCustodyBody);
            Check(failures, "bailed hood survives a still-held leader", BailedHoodSurvivesHeldLeader);
            Check(failures, "bailed hood survives a leader still crossing into booking", BailedHoodSurvivesPendingLeader);
            Check(failures, "pending hood does not re-lock a bailed leader", PendingHoodDoesNotRelockBailedLeader);
            Check(failures, "booking the leader preserves members still crossing the station threshold", PendingBookingBody);
            Check(failures, "idle escort closes the final boarding gap", BoardingEscortGap);
            Check(failures, "hidden custody bodies do not walk or repel street bodies", HiddenCustodyMovement);
            Check(failures, "short vehicle boarding approach routes around fixed props", ShortBoardingProp);
            Check(failures, "occupied car door retains a reachable physical boarding approach", OccupiedBoardingDoor);
            Check(failures, "vehicle collision sweep combines position and heading", RotatingVehicleClearance);
            Check(failures, "parking return accepts road arrival tolerance and retries distant stops", ParkingReturnTolerance);
            Check(failures, "boarding approach clears parked car and fixed props", BoardingApproachClearsCar);
            Check(failures, "new pickup cancels an old foot march while escort joins", NewBoardingCancelsOldWalk);
            Check(failures, "rejected placement validates its recomputed pose", RecomputedVehiclePose);
            Check(failures, "late booking cannot re-lock a bailed lieutenant", BookingAfterBailKeepsLeaderFree);
            Check(failures, "traffic recovery respects visibility and keeps its route", TrafficRecoveryVisibility);
            Check(failures, "traffic recovery carries the same prisoner and escort bodies", TrafficRecoveryKeepsPrisoners);
            Check(failures, "collection addresses its free detail when the lieutenant is held", CollectionUsesItsOwnDetail);
            Check(failures, "roster synchronization preserves a jailed lieutenant's free bag detail", BagDetailSurvivesHeldLeader);
            Check(failures, "swarm charges and wanted grades name the actual culprit", ScopedSwarmCharges);
            Check(failures, "officer death carries its known attacker through dispatch", OfficerDeathRetainsAttacker);
            return failures;
        }

        static void BagDetailSurvivesHeldLeader()
        {
            var previousCrews = DemoCrews.Active;
            var previousForce = PoliceForce.Instance;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var world = LivingCity.Outfit.Underworld.Deal(1987 + repetition, 2);
                    var roster = world.Of(1).Roster;
                    var crew = new Crew { Id = 1001, LieutenantId = roster.Members[1].Id };
                    crew.HoodIds.Add(roster.Members[2].Id);
                    crew.HoodIds.Add(roster.Members[3].Id);
                    roster.Crews.Clear(); roster.Crews.Add(crew);
                    Require(RosterOps.NameCollector(roster, crew.Id, roster.Members[2].Id).Ok,
                        "fixture could not assign its collector");
                    Require(RosterOps.PostEscort(roster, crew.Id, roster.Members[3].Id).Ok,
                        "fixture could not assign its escort");
                    var crews = fixture.Root.AddComponent<DemoCrews>();
                    typeof(DemoCrews).GetProperty(nameof(DemoCrews.Active)).SetValue(null, crews);
                    ((IList)Read(crews, "_houses")).Clear();
                    ((IList)Read(crews, "_houses")).Add(1);
                    Write(crews, "_root", fixture.Root.transform);
                    var force = fixture.Root.AddComponent<PoliceForce>();
                    typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance)).SetValue(null, force);
                    CrewWalker Body(int id)
                    {
                        var man = fixture.Man(); man.CharacterId = id; man.Faction = 1;
                        man.SourcePrefab = (GameObject)Call(crews, "CastFor", roster.Find(id), roster);
                        ((IDictionary)Read(crews, "_byCharacter"))[id] = man;
                        return man;
                    }
                    var boss = Body(crew.LieutenantId);
                    var collector = Body(crew.BagId);
                    var escort = Body(crew.EscortIds[0]);
                    var line = fixture.Unit(boss); line.CrewId = crew.Id; line.Faction = 1;
                    var bag = fixture.Unit(null); bag.CrewId = crew.Id; bag.Faction = 1;
                    bag.IsDetachment = true; bag.Parent = line;
                    bag.Hoods.Add(collector); bag.Hoods.Add(escort);
                    crews.Units.Add(line); crews.Units.Add(bag);
                    Call(crews, "Sync", world);
                    var collectorTransform = collector.Tf; var escortTransform = escort.Tf;
                    var collectorPosition = collector.Tf.position; var escortPosition = escort.Tf.position;
                    Require(force.Pipeline.Book(roster, boss.CharacterId, Deed.Affray, 1) != null,
                        "fixture could not book its lieutenant");
                    force.KeepCustodyAlive(boss.CharacterId);
                    for (int sync = 0; sync < 3; sync++)
                    {
                        Call(crews, "Sync", world);
                        Require(crews.BagUnitOf(crew.Id) == bag && bag.Parent == null &&
                            !bag.InCustody && bag.Hoods.Contains(collector) && bag.Hoods.Contains(escort),
                            "roster synchronization removed a jailed lieutenant's free bag detail");
                        Require(crews.BodyOf(collector.CharacterId) == collector &&
                            crews.BodyOf(escort.CharacterId) == escort &&
                            collector.Tf == collectorTransform && escort.Tf == escortTransform &&
                            collector.Tf.position == collectorPosition && escort.Tf.position == escortPosition,
                            "booking the lieutenant replaced or relocated his collector or escort");
                        Require(line.InCustody && force.KeepsCustodyAlive(boss.CharacterId),
                            "keeping the bag detail freed the booked lieutenant");
                    }
                }
            }
            finally
            {
                typeof(DemoCrews).GetProperty(nameof(DemoCrews.Active)).SetValue(null, previousCrews);
                typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance)).SetValue(null, previousForce);
            }
        }

        static void CollectionUsesItsOwnDetail()
        {
            var previousCrews = DemoCrews.Active;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var crews = fixture.Root.AddComponent<DemoCrews>();
                    var runtime = fixture.Root.AddComponent<TerritoryRuntime>();
                    Write(runtime, "crews", crews);
                    var leader = fixture.Unit(fixture.Man()); leader.CrewId = 12;
                    leader.InCustody = true;
                    var collector = fixture.Man(); collector.CharacterId = 77;
                    var detail = fixture.Unit(collector); detail.CrewId = 12; detail.IsDetachment = true;
                    crews.Units.Add(leader); crews.Units.Add(detail);
                    var own = new LivingCity.Territory.TerritoryGangId(0);
                    var group = LivingCity.Territory.TerritoryCommandNodeId.Crew(12);
                    var args = new object[] { own, group, null };
                    Require(ReferenceEquals(Call(runtime, "FindCollectionUnit", args), detail),
                        "a jailed lieutenant prevented his free collector from receiving a round");
                    crews.Units.Remove(leader);
                    Require(ReferenceEquals(Call(runtime, "FindCollectionUnit", args), detail),
                        "retiring the held line also removed the independent collector's command address");
                    args[0] = new LivingCity.Territory.TerritoryGangId(1);
                    Require(Call(runtime, "FindCollectionUnit", args) == null,
                        "another house could send the collector");
                    args[0] = own; detail.InCustody = true;
                    Require(Call(runtime, "FindCollectionUnit", args) == null,
                        "a held collector could still receive a round");
                    detail.InCustody = false; collector.Kill();
                    Require(Call(runtime, "FindCollectionUnit", args) == null,
                        "a dead collector still answered collection commands");
                }
            }
            finally { typeof(DemoCrews).GetProperty(nameof(DemoCrews.Active)).SetValue(null, previousCrews); }
        }

        sealed class ClearanceBody : IRoadUser
        {
            public Vector3 Position, Forward;
            public float HalfLen, HalfWide;
            public Vector3 RoadPosition => Position;
            public Vector3 RoadForward => Forward;
            public float RoadSpeed => 0f;
            public float HalfLength => HalfLen;
            public float HalfWidth => HalfWide;
        }

        static RoadCar ArcTestCar(LaneNet net, Carriageway road, int heading, float station, float lateral)
        {
            var car = new RoadCar { Net = net, Profile = DriverProfile.Police,
                HalfLen = 3.72353f, HalfWide = 1.28412f };
            car.Spawn(road.LaneFor(heading, lateral), heading > 0 ? station : road.Length - station);
            Write(car, "<D>k__BackingField", lateral);
            Write(car, "_pos", road.Pose(station, lateral));
            Write(car, "<Speed>k__BackingField", 0f);
            StreetTraffic.Users.Add(car);
            // Repeated fixtures can replace the same number of bodies in one editor frame.
            typeof(RoadSpace).GetMethod("Invalidate", StaticPrivate).Invoke(null, null);
            return car;
        }

        static LaneNet ArcTestRoad(float angle, out Carriageway road)
        {
            var origin = new Vector3(10000f, 0f, 10000f);
            var net = new LaneNet();
            road = net.AddRoad(origin, origin + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 100f,
                7.5f, new[] { 2.5f }, 10f, null, null, true);
            net.Finish();
            return net;
        }

        static void TurningArcRejectsParkedCorner()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                var net = ArcTestRoad(repetition * 72f, out var road);
                var car = ArcTestCar(net, road, 1, 14.13218f, 2.5f);
                var parked = ArcTestCar(net, road, -1, 11.19675f, -6.5959f);
                Write(parked, "<Parked>k__BackingField", true);
                try
                {
                    Require(!car.TryUTurn(), "turning nose entered a parked car outside the nominal lane band at repetition " + repetition);
                }
                finally { RetireCar(car); RetireCar(parked); }
            }
        }

        static void BlockedTurningArcKeepsPhysicalProgress()
        {
            foreach (float dt in new[] { .033f, .05f, .2f, .4f, .8f })
            {
                var net = ArcTestRoad(0f, out var road);
                var car = ArcTestCar(net, road, 1, 14.13218f, 2.5f);
                RoadCar parked = null;
                try
                {
                    Require(car.TryUTurn(), "clear setup did not start a turn");
                    parked = ArcTestCar(net, road, -1, 11.19675f, -6.5959f);
                    Write(parked, "<Parked>k__BackingField", true);
                    var cars = new List<RoadCar> { car };
                    for (int step = 0; step < Mathf.CeilToInt(12f / dt); step++)
                    {
                        RoadCarSimulation.Simulate(cars, dt);
                        var pose = new object[] { null, null };
                        typeof(RoadCar).GetMethod("Pose", Private).Invoke(car, pose);
                        Require(((Vector3)pose[0] - car.Position).magnitude < .02f &&
                            Vector3.Angle((Vector3)pose[1], car.Forward) < .1f,
                            "blocked turn advanced beyond its physical body at dt=" + dt);
                        Require(!RoadSpace.Overlap(car.Position, car.Forward, car.HalfLen, car.HalfWide,
                            parked.Position, parked.Forward, parked.HalfLen, parked.HalfWide, 0f, out _),
                            "turn recovery crossed the parked car");
                    }
                    Require(car.Heading == 1 && Read(car, "_man").ToString() != "UTurn",
                        "blocked turn neither reversed out nor retained its original heading at dt=" + dt +
                        " heading=" + car.Heading + " state=" + Read(car, "_man") +
                        " angle=" + Read(car, "_arcAng") + " blocked=" + Read(car, "_arcBlockedFor") +
                        " backing=" + Read(car, "_arcBacking"));
                }
                finally { RetireCar(car); if (parked != null) RetireCar(parked); }
            }
        }

        static void TurningArcFinishesWithoutYawSnap()
        {
            foreach (float dt in new[] { .033f, .05f, .2f, .4f, .8f })
            {
                var net = ArcTestRoad(0f, out var road);
                var car = ArcTestCar(net, road, 1, 40f, 2.5f);
                try
                {
                    Require(car.TryUTurn(), "clear setup did not start a turn");
                    var cars = new List<RoadCar> { car };
                    for (int step = 0; step < Mathf.CeilToInt(15f / dt) && car.Heading == 1; step++)
                    {
                        var before = car.Forward;
                        RoadCarSimulation.Simulate(cars, dt);
                        Require(Vector3.Angle(before, car.Forward) < Mathf.Max(3f, dt * 130f),
                            "turn snapped straight before its rear axle left the arc");
                    }
                    Require(car.Heading == -1, "clear turning arc did not finish");
                }
                finally { RetireCar(car); }
            }
        }

        static void SettledParkingBesideMotorcycle()
        {
            foreach (float dt in new[] { .033f, .05f, .2f, .4f, .8f })
            {
                var net = ArcTestRoad(0f, out var road);
                var bike = ArcTestCar(net, road, 1, 27.83f, 7.18f);
                bike.HalfLen = .9054f;
                bike.HalfWide = .34f;
                Write(bike, "<Parked>k__BackingField", true);
                typeof(RoadCar).GetMethod("UpdateOccupant", Private).Invoke(bike, null);
                var car = ArcTestCar(net, road, 1, 8f, 2.5f);
                try
                {
                    Require(car.GoTo(road.Pose(22f, 6.21588f), true), "parking destination refused");
                    var cars = new List<RoadCar> { car };
                    for (int step = 0; step < Mathf.CeilToInt(90f / dt) && !car.Parked; step++)
                    {
                        RoadCarSimulation.Simulate(cars, dt);
                        Require(!RoadSpace.Overlap(car.Position, car.Forward, car.HalfLen, car.HalfWide,
                            bike.Position, bike.Forward, bike.HalfLen, bike.HalfWide, 0f, out _),
                            "parking crossed the standing motorcycle");
                    }
                    Require(car.Parked && !car.HasGoal && car.S > 15f &&
                        Vector3.Dot(car.Forward, road.DirAt(car.S)) > .999f,
                        "car stayed in its final parking swing beside the motorcycle at dt=" + dt);
                }
                finally { RetireCar(car); RetireCar(bike); }
            }
        }


        static void BlockedReverseDoesNotSealClearJunction()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                var origin = new Vector3(10000f, 0f, 10000f);
                var rotation = Quaternion.Euler(0f, repetition * 72f, 0f);
                Vector3 At(float lateral, float station) => origin + rotation * new Vector3(lateral, 0f, station);
                var net = new LaneNet();
                var centre = At(0f, 42.5f);
                var node = net.AddNode(centre.x, centre.z, 7.5f, 7.5f, 5.7f);
                var road = net.AddRoad(At(0f, 0f), At(0f, 35f), 7.5f,
                    new[] { 2.5f }, 10f, null, node, true);
                net.AddRoad(At(0f, 50f), At(0f, 150f), 7.5f,
                    new[] { 2.5f }, 10f, node, null, true);
                net.Finish();
                var other = ArcTestCar(net, road, 1, 21.63818f, 5.064204f);
                Write(other, "_pos", At(5.422153f, 21.4914f));
                Write(other, "_fwd", rotation * new Vector3(-.35644624f, 0f, .93431586f));
                typeof(RoadCar).GetMethod("UpdateOccupant", Private).Invoke(other, null);
                var car = ArcTestCar(net, road, 1, 28.6506f, 2.5f);
                Write(car, "_beltFor", 1f);
                try
                {
                    var start = car.Position;
                    var cars = new List<RoadCar> { car };
                    for (int step = 0; step < 900 && (car.Position - start).sqrMagnitude < 400f; step++)
                    {
                        RoadCarSimulation.Simulate(cars, .033f);
                        Require(!RoadSpace.Overlap(car.Position, car.Forward, car.HalfLen, car.HalfWide,
                            other.Position, other.Forward, other.HalfLen, other.HalfWide, 0f, out _),
                            "forward crossing collided with the body behind the stop line");
                    }
                    Require((car.Position - start).sqrMagnitude > 400f,
                        "failed reverse kept a physically clear junction sealed at repetition " + repetition);
                }
                finally { RetireCar(car); RetireCar(other); }
            }
        }


        static void BlockedPoliceChallengePoint()
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
                plan.Take(new SidewalkPlan.Box { C = new Vector2(wanted.x, wanted.z), H = new Vector2(.4f, .4f),
                    Ax = new Vector2(side.x, side.z), Az = new Vector2(forward.x, forward.z), Solid = true });
                WalkObstacles.RegisterPlan(plan);
                try
                {
                    beat.Challenge(suspect);
                    Require(lead.HasOrder, "blocked challenge point left the patrol idle outside arrest reach");
                    Require(!WalkObstacles.Standing(lead.OrderDestination, WalkRoute.ClearanceRadius) &&
                        (lead.OrderDestination - origin).magnitude < 4.6f,
                        "challenge destination is occupied or outside actual question reach");
                    for (int step = 0; step < 120 && !beat.StoodOver; step++) lead.TickCrew(.033f);
                    Require(beat.StoodOver, "patrol did not physically close the last gap to ask its question");
                }
                finally { WalkObstacles.UnregisterPlan(plan); }
            }
        }


        static void RotatingVehicleClearance()
        {
            var invalidate = typeof(RoadSpace).GetMethod("Invalidate", StaticPrivate);
            for (int repetition = 0; repetition < 5; repetition++)
            {
                var rotation = Quaternion.Euler(0, repetition * 72f, 0);
                var origin = new Vector3(10000f, 0, 10000f);
                var self = new ClearanceBody { Position = origin,
                    Forward = rotation * new Vector3(-.297910064f, 0, -.954593956f),
                    HalfLen = 3.723523f, HalfWide = 1.28414917f };
                var other = new ClearanceBody {
                    Position = origin + rotation * new Vector3(-5.404983f, 0, -4.7402f),
                    Forward = rotation * Vector3.right, HalfLen = 3.723584f, HalfWide = 1.28414917f };
                StreetTraffic.Users.Add(self); StreetTraffic.Users.Add(other);
                try
                {
                    invalidate.Invoke(null, null);
                    var to = origin + rotation * new Vector3(.106917f, 0, .124777f);
                    var facing = rotation * new Vector3(-.269077569f, 0, -.963118553f);
                    var moved = RoadSpace.Advance(self, origin, to, facing,
                        self.HalfLen, self.HalfWide, out var hit);
                    Require(hit == null && (moved - to).sqrMagnitude < .00001f,
                        "future orientation invented a collision at the old vehicle position");

                    self.Forward = rotation * Vector3.forward;
                    other.Position = origin + rotation * new Vector3(2.5f, 0, 2.5f);
                    other.HalfLen = other.HalfWide = .2f;
                    invalidate.Invoke(null, null);
                    RoadSpace.Advance(self, origin, origin, rotation * Vector3.right,
                        self.HalfLen, self.HalfWide, out hit);
                    Require(hit == other, "a stationary turn skipped its occupied intermediate angle");
                }
                finally
                {
                    StreetTraffic.Users.Remove(self); StreetTraffic.Users.Remove(other);
                    invalidate.Invoke(null, null);
                }
            }
        }

        static void OccupiedBoardingDoor()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var crews = fixture.Root.AddComponent<DemoCrews>();
                var man = fixture.Man();
                var origin = man.Tf.position;
                var turn = Quaternion.Euler(0, repetition * 72f, 0);
                var direction = turn * Vector3.forward;
                var side = turn * Vector3.right;
                var door = origin + direction * 8f;
                var plan = new SidewalkPlan();
                plan.Take(new SidewalkPlan.Box {
                    C = new Vector2(door.x, door.z), H = new Vector2(.6f, .6f),
                    Ax = new Vector2(side.x, side.z), Az = new Vector2(direction.x, direction.z), Solid = true,
                });
                WalkObstacles.RegisterPlan(plan);
                try
                {
                    Require(WalkObstacles.Standing(door, WalkRoute.ClearanceRadius),
                        "boarding fixture did not occupy the exact door mark");
                    typeof(PrisonerCarriage).GetMethod("OrderBoarderToDoor", StaticPrivate)
                        .Invoke(null, new object[] { crews, man, door, true });
                    Require(man.HasOrder, "a prop at the door left the boarder without a route");
                    var route = (List<Vector3>)Read(man, "_legs");
                    var previous = origin;
                    foreach (var corner in route)
                    {
                        Require(!WalkObstacles.BlocksStanding(previous, corner, WalkRoute.ClearanceRadius),
                            "boarding approach crosses fixed geometry");
                        previous = corner;
                    }
                    if (route.Count == 0)
                    {
                        previous = man.OrderDestination;
                        Require(!WalkObstacles.BlocksStanding(origin, previous, WalkRoute.ClearanceRadius),
                            "the direct boarding approach crosses fixed geometry");
                    }
                    Require((previous - door).magnitude <= 2.01f && man.Tf.position == origin,
                        "door avoidance moved the body or moved the approach outside boarding reach");
                    var boarding = new PrisonerCarriage.BoardingMan {
                        Man = man, GeometryReady = true, Door = door, RetryAt = Time.time + 10f,
                    };
                    Require(!PrisonerCarriage.TickOfficerBoarding(boarding, crews, null, null) &&
                        !boarding.Seated && boarding.Door == door,
                        "finding an approach replaced the real door or seated a distant officer");
                }
                finally { WalkObstacles.UnregisterPlan(plan); }
            }
        }

        static void ShortBoardingProp()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var crews = fixture.Root.AddComponent<DemoCrews>();
                var man = fixture.Man();
                var origin = man.Tf.position;
                var turn = Quaternion.Euler(0, repetition * 72f, 0);
                var direction = turn * Vector3.forward;
                var goal = origin + direction * 4f;
                var centre = origin + direction * 2f;
                var side = turn * Vector3.right;
                var plan = new SidewalkPlan();
                plan.Take(new SidewalkPlan.Box {
                    C = new Vector2(centre.x, centre.z), H = new Vector2(.7f, .35f),
                    Ax = new Vector2(side.x, side.z), Az = new Vector2(direction.x, direction.z), Solid = true,
                });
                WalkObstacles.RegisterPlan(plan);
                try
                {
                    Require(WalkObstacles.BlocksStanding(origin, goal, WalkRoute.ClearanceRadius),
                        "boarding fixture did not obstruct the short direct chord");
                    Call(crews, "SendToVehicleDoor", man, goal, 0f, false);
                    var route = (List<Vector3>)Read(man, "_legs");
                    Require(man.HasOrder && route.Count > 1,
                        "a short boarding approach repeated a direct order through the prop");
                    var previous = origin;
                    foreach (var corner in route)
                    {
                        Require(!WalkObstacles.BlocksStanding(previous, corner, WalkRoute.ClearanceRadius),
                            "boarding route cut through the fixed prop");
                        previous = corner;
                    }
                    Require((previous - goal).sqrMagnitude < .01f && man.Tf.position == origin,
                        "boarding route changed its door or replaced walking with relocation");
                }
                finally { WalkObstacles.UnregisterPlan(plan); }
            }
        }

        static void BoardingEscortGap()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var man = fixture.Man();
                var escort = fixture.Man();
                var direction = Quaternion.Euler(0, repetition * 72f, 0) * Vector3.forward;
                var door = man.Tf.position + direction * 2.75f;
                escort.Tf.position = door + direction * LivingCity.Police.PoliceProcedure.CustodyEscortCarClearance;
                var carRoot = new GameObject("Boarding car");
                carRoot.transform.SetParent(fixture.Root.transform);
                carRoot.transform.position = door - Vector3.Cross(Vector3.up, direction) * 2f;
                var car = new RoadCar { Tf = carRoot.transform };
                try
                {
                    var boarding = new PrisonerCarriage.BoardingMan {
                        Man = man, Escort = escort, Car = car, Seat = 2, Prisoner = true,
                        Started = true, GeometryReady = true, Door = door,
                        EscortPost = escort.Tf.position, RetryAt = Time.time - 1f,
                    };
                    Require(!PrisonerCarriage.TickPrisonerBoarding(boarding, null, null, null, null),
                        "prisoner was seated before the escort closed the gap");
                    Require(escort.HasOrder &&
                        (escort.OrderDestination - man.Tf.position).sqrMagnitude < 3.2f * 3.2f,
                        "idle escort at its covering post never approached the waiting prisoner");
                    var goal = escort.OrderDestination;
                    PrisonerCarriage.TickPrisonerBoarding(boarding, null, null, null, null);
                    Require(escort.HasOrder && escort.OrderDestination == goal && !boarding.Seated,
                        "boarding restarted a live closing route or seated at a distance");
                }
                finally
                {
                    // Fixture owns the edit-mode GameObject and destroys it immediately.
                    car.Tf = null;
                    car.Vanish();
                }
            }
        }

        static void HiddenCustodyMovement()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var crews = fixture.Root.AddComponent<DemoCrews>();
                var inside = fixture.Man();
                var outside = fixture.Man();
                var unit = fixture.Unit(inside);
                unit.Hoods.Add(outside); crews.Units.Add(unit);
                var original = inside.Tf.position;
                var direction = Quaternion.Euler(0, repetition * 72f, 0) * Vector3.forward;
                inside.OrderToPoint(original + direction * 10f);
                inside.Tf.gameObject.SetActive(false);
                inside.EaseAside(direction, 1f);
                inside.TickCrew(.25f);
                Require(inside.Tf.position == original,
                    "inactive body still moved through its old street stride or separation nudge");
                Call(crews, "Separate", .25f);
                Require(outside.Tf.position == original,
                    "an inactive body pushed the visible street walker away");
            }
        }

        static void PendingBookingBody()
        {
            var forceInstance = typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance));
            var previousForce = PoliceForce.Instance;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var world = LivingCity.Outfit.Underworld.Deal(1987 + repetition, 2);
                    var roster = world.Of(repetition % 2).Roster;
                    while (roster.Members.Count < 2)
                        RosterSeeder.Recruit(roster, new System.Random(1987 + repetition));
                    var crews = fixture.Root.AddComponent<DemoCrews>();
                    ((IList)Read(crews, "_houses")).Clear();
                    Write(crews, "_root", fixture.Root.transform);
                    var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
                    var force = fixture.Root.AddComponent<PoliceForce>();
                    forceInstance.SetValue(null, force);
                    force.Init(dispatch);
                    var boss = fixture.Man();
                    boss.CharacterId = roster.Members[0].Id; boss.Faction = roster.GangId;
                    var hood = fixture.Man();
                    hood.CharacterId = roster.Members[1].Id; hood.Faction = roster.GangId;
                    var unit = fixture.Unit(boss);
                    unit.Faction = roster.GangId;
                    unit.Hoods.Add(hood);
                    unit.InCustody = unit.CustodyTracked = unit.Surrendered = true;
                    crews.Units.Add(unit);
                    var bodies = (IDictionary)Read(crews, "_byCharacter");
                    bodies[boss.CharacterId] = boss; bodies[hood.CharacterId] = hood;
                    var custodyType = typeof(PoliceDispatch).GetNestedType("Custody", BindingFlags.NonPublic);
                    var prisonerType = typeof(PoliceDispatch).GetNestedType("CustodyPrisoner", BindingFlags.NonPublic);
                    var custody = Activator.CreateInstance(custodyType, true);
                    custodyType.GetField("Crew").SetValue(custody, unit);
                    var pending = Activator.CreateInstance(prisonerType, true);
                    prisonerType.GetField("Man").SetValue(pending, hood);
                    prisonerType.GetField("CharacterId").SetValue(pending, hood.CharacterId);
                    ((IList)custodyType.GetField("Prisoners").GetValue(custody)).Add(pending);
                    ((IList)Read(dispatch, "_custodies")).Add(custody);
                    Require(force.Pipeline.Book(roster, boss.CharacterId, Deed.Affray, 1) != null,
                        "pending booking fixture could not book its leader");
                    force.KeepCustodyAlive(boss.CharacterId);
                    var original = hood.Tf.position;
                    Call(crews, "Sync", world);
                    Require(crews.BodyOf(hood.CharacterId) == hood && hood.Tf != null &&
                        hood.Tf.position == original && unit.Hoods.Contains(hood),
                        "booking the leader retired the member still walking into the station");
                    Require(!force.KeepsCustodyAlive(hood.CharacterId),
                        "unbooked body was prematurely entered into long-term custody tracking");
                    Require(force.Pipeline.Book(roster, hood.CharacterId, Deed.Affray, 1) != null,
                        "preserved hood could not be booked");
                    force.KeepCustodyAlive(hood.CharacterId);
                    prisonerType.GetField("Booked").SetValue(pending, true);
                    custodyType.GetField("Finished").SetValue(custody, true);
                    Call(crews, "Sync", world);
                    Require(crews.BodyOf(hood.CharacterId) == hood && hood.Tf != null,
                        "finishing the arrest discarded the newly booked body");
                }
            }
            finally { forceInstance.SetValue(null, previousForce); }
        }

        static void BailedHoodSurvivesHeldLeader() => BailedHoodSurvivesLeader(false);

        static void BailedHoodSurvivesPendingLeader() => BailedHoodSurvivesLeader(true);

        static void BailedHoodSurvivesLeader(bool pendingLeader)
        {
            var active = typeof(DemoCrews).GetProperty(nameof(DemoCrews.Active));
            var forceInstance = typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance));
            var previousCrews = DemoCrews.Active;
            var previousForce = PoliceForce.Instance;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var world = LivingCity.Outfit.Underworld.Deal(1987 + repetition, 2);
                    var roster = world.Of(1).Roster;
                    var crews = fixture.Root.AddComponent<DemoCrews>();
                    active.SetValue(null, crews);
                    ((IList)Read(crews, "_houses")).Clear();
                    Write(crews, "_root", fixture.Root.transform);
                    var force = fixture.Root.AddComponent<PoliceForce>();
                    forceInstance.SetValue(null, force);
                    var boss = fixture.Man(); boss.CharacterId = roster.Members[0].Id; boss.Faction = 1;
                    var hood = fixture.Man(); hood.CharacterId = roster.Members[1].Id; hood.Faction = 1;
                    var unit = fixture.Unit(boss); unit.Faction = 1; unit.Hoods.Add(hood);
                    unit.InCustody = unit.CustodyTracked = unit.Surrendered = true;
                    crews.Units.Add(unit);
                    var bodies = (IDictionary)Read(crews, "_byCharacter");
                    bodies[boss.CharacterId] = boss; bodies[hood.CharacterId] = hood;
                    if (pendingLeader)
                    {
                        var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
                        force.Init(dispatch);
                        var custodyType = typeof(PoliceDispatch).GetNestedType("Custody", BindingFlags.NonPublic);
                        var prisonerType = typeof(PoliceDispatch).GetNestedType("CustodyPrisoner", BindingFlags.NonPublic);
                        var custody = Activator.CreateInstance(custodyType, true);
                        custodyType.GetField("Crew").SetValue(custody, unit);
                        var pending = Activator.CreateInstance(prisonerType, true);
                        prisonerType.GetField("Man").SetValue(pending, boss);
                        prisonerType.GetField("CharacterId").SetValue(pending, boss.CharacterId);
                        ((IList)custodyType.GetField("Prisoners").GetValue(custody)).Add(pending);
                        ((IList)Read(dispatch, "_custodies")).Add(custody);
                    }
                    else
                    {
                        force.Pipeline.Book(roster, boss.CharacterId, Deed.Affray, 1);
                        force.KeepCustodyAlive(boss.CharacterId);
                    }
                    var prisoner = force.Pipeline.Book(roster, hood.CharacterId, Deed.Affray, 1);
                    force.KeepCustodyAlive(hood.CharacterId);
                    Require(force.Pipeline.PostBail(roster, prisoner, 2000, 1), "fixture could not bail its hood");
                    var original = hood.Tf.position;
                    force.ReleaseCustodyTracking(hood.CharacterId, original, relocate: false);
                    Require(unit.InCustody && unit.CustodyTracked, "hood bail briefly unlocked the held or pending leader");
                    for (int sync = 0; sync < 2; sync++)
                    {
                        Call(crews, "Sync", world);
                        var owner = crews.UnitOf(hood);
                        Require(crews.BodyOf(hood.CharacterId) == hood && hood.Tf != null &&
                            hood.Tf.position == original, "bailing a hood before his leader removed or relocated the body");
                        Require(owner != null && !owner.InCustody && !owner.CustodyTracked &&
                            !owner.Surrendered && !unit.Hoods.Contains(hood),
                            "freed hood still belonged to the held leader's custody unit");
                        Require(unit.InCustody && (force.KeepsCustodyAlive(boss.CharacterId) ||
                            (bool)Call(force, "KeepsUnbookedBody", boss.CharacterId)),
                            "bailing the hood unlocked the held leader");
                    }
                    prisoner.Stage = LivingCity.Police.PrisonStage.Sentenced;
                    roster.Find(hood.CharacterId).Status = CharacterStatus.Jailed;
                    force.KeepCustodyAlive(hood.CharacterId);
                    Call(crews, "Sync", world);
                    Require(crews.BodyOf(hood.CharacterId) == hood && crews.UnitOf(hood)?.InCustody == true,
                        "conviction after the hood's bail lost his original body");
                    prisoner.Stage = LivingCity.Police.PrisonStage.Cleared;
                    roster.Find(hood.CharacterId).Status = CharacterStatus.Active;
                    force.ReleaseCustodyTracking(hood.CharacterId, original, relocate: false);
                    Call(crews, "Sync", world);
                    Require(crews.BodyOf(hood.CharacterId) == hood && crews.UnitOf(hood)?.InCustody == false,
                        "release after the sentence removed the unled hood again");
                }
            }
            finally { active.SetValue(null, previousCrews); forceInstance.SetValue(null, previousForce); }
        }

        static void OrphanedCustodyBody()
        {
            var forceInstance = typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance));
            var previousForce = PoliceForce.Instance;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var world = LivingCity.Outfit.Underworld.Deal(1987 + repetition, 2);
                    var roster = world.Of(repetition % 2).Roster;
                    if (roster.Members.Count < 2)
                        RosterSeeder.Recruit(roster, new System.Random(1987 + repetition));
                    var member = roster.Members[1];
                    var crews = fixture.Root.AddComponent<DemoCrews>();
                    ((IList)Read(crews, "_houses")).Clear();
                    Write(crews, "_root", fixture.Root.transform);
                    var force = fixture.Root.AddComponent<PoliceForce>();
                    forceInstance.SetValue(null, force);
                    var man = fixture.Man();
                    man.CharacterId = member.Id; man.Faction = roster.GangId;
                    var oldRoot = new GameObject("Former active crew");
                    oldRoot.transform.SetParent(fixture.Root.transform);
                    man.Tf.SetParent(oldRoot.transform, true);
                    var original = man.Tf.position;
                    ((IDictionary)Read(crews, "_byCharacter"))[member.Id] = man;
                    var prisoner = force.Pipeline.Book(roster, member.Id, LivingCity.Personnel.Deed.Affray, 1);
                    Require(prisoner != null, "orphaned custody fixture could not book its hood");
                    force.KeepCustodyAlive(member.Id);
                    DoorBeat.RestoreInside(man, original);
                    // The active lieutenant's membership has already been rebuilt;
                    // this held hood remains under the former unit's transform.
                    Call(crews, "Sync", world);
                    var owner = crews.UnitOf(man);
                    Require(owner != null && owner.InCustody && owner.CustodyTracked,
                        "held hood lost its physical custody owner when the active crew was rebuilt");
                    Object.DestroyImmediate(oldRoot);
                    Require(crews.BodyOf(member.Id) == man && man.Tf != null &&
                        man.Tf.position == original && DoorBeat.Held(man),
                        "retiring the former crew deleted, relocated or exposed its held hood");
                    Call(crews, "Sync", world);
                    Require(crews.UnitOf(man) == owner && crews.Units.Count == 1,
                        "the next roster projection duplicated the custody view");
                }
            }
            finally { forceInstance.SetValue(null, previousForce); }
        }

        static void BailedConvictionBody()
        {
            var active = typeof(DemoCrews).GetProperty(nameof(DemoCrews.Active));
            var forceInstance = typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance));
            var previousCrews = DemoCrews.Active;
            var previousForce = PoliceForce.Instance;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var world = LivingCity.Outfit.Underworld.Deal(1987 + repetition, 2);
                    var roster = world.Of(repetition % 2).Roster;
                    if (roster.Members.Count < 2)
                        RosterSeeder.Recruit(roster, new System.Random(1987 + repetition));
                    var id = roster.Members[1].Id;
                    var crews = fixture.Root.AddComponent<DemoCrews>();
                    // Only the preserved custody body participates; this fixture
                    // has no pedestrian graph for spawning unrelated active men.
                    ((IList)Read(crews, "_houses")).Clear();
                    var force = fixture.Root.AddComponent<PoliceForce>();
                    active.SetValue(null, crews);
                    forceInstance.SetValue(null, force);
                    var body = fixture.Man(); body.CharacterId = id;
                    body.Tf.position += Vector3.up * crews.GroundY;
                    var original = body.Tf.position;
                    var unit = fixture.Unit(body); unit.Faction = roster.GangId;
                    crews.Units.Add(unit);
                    ((IDictionary)Read(crews, "_byCharacter"))[id] = body;
                    var file = force.Pipeline.OpenCase(LivingCity.Personnel.Deed.Affray, 1, 0, 1);
                    var prisoner = force.Pipeline.Book(roster, id, LivingCity.Personnel.Deed.Affray, 0, file);
                    Require(prisoner != null, "sentence fixture could not book its defendant");
                    // Reproduce the daily verdict edge after bail removed the pin.
                    prisoner.Stage = LivingCity.Police.PrisonStage.Sentenced;
                    Require(!force.KeepsCustodyAlive(id), "sentence fixture was already pinned");
                    Call(force, "AnnounceVerdict", roster, prisoner, LivingCity.Police.CaseStatus.Tried, false);
                    Require(force.KeepsCustodyAlive(id), "conviction left the freed body's custody pin absent");
                    Call(crews, "Sync", world);
                    Require(crews.BodyOf(id) == body && body.Tf != null && body.Tf.position == original,
                        "roster synchronization removed or relocated the newly sentenced body");
                    Require(!crews.OrderUnit(crews.UnitOf(body), original + Vector3.right * 10f, out _),
                        "the sentenced defendant still accepted an ordinary player move");
                    var outside = original + Vector3.forward * (8f + repetition);
                    DoorBeat.RestoreInside(body, outside);
                    body.Tf.position = original;
                    var pickup = (Vector3)typeof(PoliceForce).GetMethod("PrisonerDoor", StaticPrivate)
                        .Invoke(null, new object[] { body, original + Vector3.left * 100f });
                    Require(pickup == outside && body.Tf.position == original,
                        "transfer chose its sending station instead of the prisoner's real exit");
                }
            }
            finally { active.SetValue(null, previousCrews); forceInstance.SetValue(null, previousForce); }
        }

        static void RetaskFinishedJob()
        {
            using var fixture = new Fixture();
            var crews = fixture.Root.AddComponent<DemoCrews>();
            var unit = fixture.Unit(fixture.Man());
            unit.Boss.Tf.position += Vector3.up * crews.GroundY;
            unit.CrewId = 987654;
            var dispatched = (IDictionary)typeof(CrewJobs).GetField("Dispatched", StaticPrivate).GetValue(null);
            var driving = (IDictionary)typeof(CrewJobs).GetField("Driving", StaticPrivate).GetValue(null);
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    // A foot job and a driving job can both leave a return stamp.
                    dispatched[unit.CrewId] = repetition;
                    driving[unit.CrewId] = repetition;
                    Require(CrewJobs.MarchOutstanding(unit.CrewId), "missing completed-job travel fixture");
                    var destination = unit.Position + Quaternion.Euler(0f, repetition * 72f, 0f) * Vector3.forward * 10f;
                    Require(crews.OrderUnit(unit, destination, out var accepted), "new player move was refused");
                    Require(!CrewJobs.MarchOutstanding(unit.CrewId),
                        "a new player order left the old job's automatic return armed");
                    Require(unit.Boss.HasOrder && (unit.Boss.OrderDestination - accepted).sqrMagnitude < .01f,
                        $"retasking discarded destination {accepted}: {unit.Boss.State}, goal {unit.Boss.OrderDestination}, repeat {repetition}");
                }
            }
            finally { CrewJobs.ForgetDispatch(unit.CrewId); }
        }

        static void SharedShelterPassage() => CheckSharedShelterPassage(false);
        static void SmashedShelterPassage() => CheckSharedShelterPassage(true);

        static void CheckSharedShelterPassage(bool smashed)
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var root = new GameObject("Shared shelter door");
                root.transform.SetParent(fixture.Root.transform);
                var front = root.AddComponent<Storefront>();
                if (smashed) Write(front, "damageState", StorefrontState.Smashed);
                ((IList)Read(front, "leaves")).Add(root.transform);
                ((IList)Read(front, "leafClosed")).Add(Quaternion.identity);
                var first = fixture.Man(); var second = fixture.Man();
                first.Tf.position = second.Tf.position = new Vector3(10400f, 0f, 10400f + repetition * 10f);
                var outside = first.Tf.position;
                var inside = outside + Quaternion.Euler(0f, repetition * 72f, 0f) * Vector3.forward * 2f;
                foreach (var man in new[] { first, second })
                    DoorBeat.VisitThrough(man, outside, (outside + inside) * .5f, inside, root.transform, hold: true);
                Require((float)Read(front, "doorTarget") == 1f,
                    "a physical visitor cannot open the shop's entrance");
                var beat = (DoorBeat)typeof(DoorBeat).GetField("instance", StaticPrivate).GetValue(null);
                var calls = (IList)Read(beat, "calls");
                object Visit(CrewWalker man)
                {
                    foreach (var candidate in calls)
                        if (candidate.GetType().GetField("Man").GetValue(candidate) == man) return candidate;
                    return null;
                }
                var one = Visit(first); var two = Visit(second);
                Require(one != null && two != null && one != two,
                    "a second visitor was merged into another man's doorway call");
                Require(ReferenceEquals(one.GetType().GetField("Swing").GetValue(one),
                    two.GetType().GetField("Swing").GetValue(two)), "one physical door has competing swing owners");
                void Tick(object visit) => Call(beat, "TickThrough", calls.IndexOf(visit), visit);
                void Refresh() => Call(beat, "RefreshPassages", 0f);
                // Reproduce the live ordering: the first enters just as the second
                // reaches OpeningEntry, so his Close used to strand the second man.
                Write(front, "doorAmount", 1f);
                Tick(one);
                first.Tf.position = inside;
                Tick(one);
                Refresh();
                Require(DoorBeat.Held(first) && !DoorBeat.Held(second) && (float)Read(front, "doorTarget") == 1f,
                    "first arrival closed the shared door on its waiting visitor");
                Tick(two); second.Tf.position = inside; Tick(two); Refresh();
                Require(DoorBeat.Held(first) && DoorBeat.Held(second) && (float)Read(front, "doorTarget") == 0f,
                    "both visitors did not enter and close the shared door");
                DoorBeat.SendOut(first); DoorBeat.SendOut(second);
                Tick(one); Tick(two); Refresh();
                Write(front, "doorAmount", 1f);
                Tick(one); Tick(two);
                first.Tf.position = outside; Tick(one); Refresh();
                Require((float)Read(front, "doorTarget") == 1f,
                    "first departure closed the shared door on the second exit");
                second.Tf.position = outside; Tick(two); Refresh();
                Require(first.Tf.gameObject.activeSelf && second.Tf.gameObject.activeSelf &&
                    (float)Read(front, "doorTarget") == 0f, "shared doorway did not release both visible bodies");
                Require(!smashed || front.State == StorefrontState.Smashed,
                    "using the damaged entrance silently repaired its broken windows");
                ((IList)Read(front, "leaves")).Clear();
            }
        }

        static void StableJunctionWaitingPriority()
        {
            using var fixture = new Fixture();
            var net = new LaneNet();
            var centre = net.AddNode(10100f, 10100f, 7.5f, 7.5f, 5.7f);
            centre.Signal = new TrafficSignal(0f);
            foreach (var direction in new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back })
            {
                var outside = net.AddNode(centre.X + direction.x * 100f,
                    centre.Z + direction.z * 100f, 7.5f, 7.5f, 5.7f);
                var from = new Vector3(centre.X, 0f, centre.Z) + direction * 7.5f;
                var to = new Vector3(outside.X, 0f, outside.Z) - direction * 7.5f;
                net.AddRoad(from, to, 7.5f, new[] { 2.5f }, 10f, centre, outside, Mathf.Abs(direction.z) > .5f);
            }
            net.Finish();
            Connector first = null, second = null;
            foreach (var a in centre.Connectors)
                foreach (var b in centre.Connectors)
                    if (first == null && a.From != b.From && a.From.NorthSouth == b.From.NorthSouth &&
                        b.Index < a.Conflicts.Length && a.Conflicts[b.Index])
                    { first = a; second = b; }
            Require(first != null, "no conflicting approaches for waiting priority fixture");
            foreach (float dt in new[] { 1f / 30f, .05f, .2f, .8f, 1.6f })
            {
                var earlier = new RoadCar { Net = net, Tf = fixture.Man().Tf, HalfLen = 2.3f, HalfWide = .95f };
                var later = new RoadCar { Net = net, Tf = fixture.Man().Tf, HalfLen = 2.3f, HalfWide = .95f };
                try
                {
                    var cars = new[] { earlier, later };
                    var paths = new[] { first, second };
                    for (int i = 0; i < cars.Length; i++)
                    {
                        var car = cars[i]; var path = paths[i];
                        car.Spawn(path.From, path.From.Length - car.HalfLen - centre.StopSetback);
                        car.Route = new Dictionary<RoadEdge, RoadEdge> { [path.From] = path.To };
                        Call(car, "PlanNext", centre);
                        Write(car, "<Speed>k__BackingField", 0f);
                        Write(car, "_heldAtLine", TrafficSignal.Cycle + 10f);
                        Write(car, "_waitingLineAt", Time.time - TrafficSignal.Cycle - 10f);
                    }
                    Require((bool)Call(later, "YieldsToWaitingApproach", centre),
                        "later id did not yield an equal-time arrival");
                    Write(later, "_heldAtLine", TrafficSignal.Cycle + 10f + dt);
                    Require(!(bool)Call(earlier, "YieldsToWaitingApproach", centre),
                        "both cars yielded after only one elapsed counter advanced at dt=" + dt);
                }
                finally { RetireCar(earlier); RetireCar(later); }
            }
        }

        static void UnwedgePreservesTravel()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var at = man.Tf.position;
            var unwedge = typeof(DemoCrews).GetMethod("Unwedge", StaticPrivate);
            foreach (float angle in new[] { 0f, 30f, 90f, 135f, 270f })
            {
                var rotation = Quaternion.Euler(0f, angle, 0f);
                var centre = at + rotation * new Vector3(-.07f, 0f, .43f);
                var plan = new SidewalkPlan();
                plan.Take(SidewalkPlan.Make(new Vector2(centre.x, centre.z), angle, Vector2.one * .17f, true));
                WalkObstacles.RegisterPlan(plan);
                try
                {
                    Require(!WalkObstacles.Standing(at, WalkRoute.ClearanceRadius) &&
                        WalkObstacles.Standing(at, WalkObstacles.Radius), "pole clearance setup failed");
                    Require(!(bool)unwedge.Invoke(null, new object[] { man }) && man.Tf.position == at,
                        "crew recovery relocated a body from a proved walking point");
                }
                finally { WalkObstacles.UnregisterPlan(plan); man.Tf.position = at; }
            }
        }

        static void UnwedgeConnectedRecovery()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var at = man.Tf.position;
            var unwedge = typeof(DemoCrews).GetMethod("Unwedge", StaticPrivate);
            foreach (float angle in new[] { 0f, 30f, 90f, 135f, 270f })
            {
                var centre = at + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * .34f;
                var plan = new SidewalkPlan();
                plan.Take(SidewalkPlan.Make(new Vector2(centre.x, centre.z), angle, Vector2.one * .17f, true));
                WalkObstacles.RegisterPlan(plan);
                try
                {
                    Require((bool)unwedge.Invoke(null, new object[] { man }), "shallow overlap was not repaired");
                    Require(!WalkObstacles.Standing(man.Tf.position, WalkObstacles.Radius) &&
                        !WalkObstacles.BlocksStanding(at, man.Tf.position, .1f), "recovery crossed the pole core");
                    man.Tf.position = centre;
                    Require(!(bool)unwedge.Invoke(null, new object[] { man }) && man.Tf.position == centre,
                        "a centre inside solid geometry was teleported through it");
                }
                finally { WalkObstacles.UnregisterPlan(plan); man.Tf.position = at; }
            }
        }

        static void ParkedGlowFootprint()
        {
            using var fixture = new Fixture();
            var car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            car.transform.SetParent(fixture.Root.transform, false);
            var markers = new GameObject("parked-marker-glow");
            markers.transform.SetParent(car.transform, false);
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.transform.SetParent(markers.transform, false);
            bulb.transform.localPosition = Vector3.right * 5f;
            for (int i = 1; i <= 5; i++)
            {
                car.transform.localScale = Vector3.one * i;
                var foot = PropFootprint.Of(car);
                Require(foot.Centre.sqrMagnitude < .0001f &&
                    (foot.Half - Vector2.one * (.5f * i)).sqrMagnitude < .0001f,
                    "a decorative marker enlarged the body during a fresh footprint measurement");
            }
        }

        static void ParkedGlowRecycling()
        {
            using var fixture = new Fixture();
            var glow = fixture.Root.AddComponent<DemoParkedCarGlow>();
            var payload = new GameObject("streamed payload");
            payload.transform.SetParent(fixture.Root.transform, false);
            var car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            car.name = "SM_Veh_Test_Car";
            car.transform.SetParent(payload.transform, false);
            var hash = typeof(DemoParkedCarGlow).GetMethod("Hash01", StaticPrivate);
            Vector3 lit = default, unlit = default;
            bool haveLit = false, haveUnlit = false;
            for (int i = 0; i < 100 && (!haveLit || !haveUnlit); i++)
            {
                var at = new Vector3(10000f + i, 0f, 10000f);
                float roll = (float)hash.Invoke(null, new object[] { at, 0x51ED270Bu });
                if (roll < .16f) { lit = at; haveLit = true; }
                else { unlit = at; haveUnlit = true; }
            }
            Require(haveLit && haveUnlit, "no deterministic lit and unlit positions");
            for (int i = 0; i < 5; i++)
            {
                car.transform.position = unlit;
                glow.Register(payload.transform);
                Require(car.transform.childCount == 0, "unlit vehicle acquired marker objects");
                glow.Unregister(payload.transform);
                Require(((HashSet<Transform>)Read(glow, "_seen")).Count == 0, "unlit registration survived eviction");
                car.transform.position = lit;
                glow.Register(payload.transform);
                glow.Register(payload.transform);
                Require(car.transform.childCount == 1, "registration duplicated or omitted markers");
                glow.Unregister(payload.transform);
                Require(car.transform.childCount == 0 && ((HashSet<Transform>)Read(glow, "_seen")).Count == 0,
                    "pooled body retained marker objects or its old registration");
            }
        }

        static void CarriedPropIsNotStatic()
        {
            using var fixture = new Fixture();
            var life = fixture.Root.AddComponent<ResidentialBlockLife>();
            var carrier = new GameObject("Resident");
            carrier.transform.SetParent(fixture.Root.transform);
            carrier.transform.position = new Vector3(10000f, .5f, 10000f);
            var carried = GameObject.CreatePrimitive(PrimitiveType.Cube);
            carried.name = "SM_Prop_Rubbish";
            carried.transform.SetParent(carrier.transform, false);
            var actorType = typeof(ResidentialBlockLife).GetNestedType("Actor", BindingFlags.NonPublic);
            var actor = Activator.CreateInstance(actorType, true);
            actorType.GetField("Root").SetValue(actor, carrier);
            ((IList)Read(life, "_actors")).Add(actor);
            var grounded = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grounded.name = "SM_Prop_GroundedBin";
            grounded.transform.SetParent(fixture.Root.transform);
            grounded.transform.position = new Vector3(10004f, .5f, 10000f);
            var plan = WalkObstacles.ComposedPropPlan(fixture.Root.transform, 0f);
            Require(plan.Count == 1 && plan.Boxes[0].SourceName == grounded.name,
                "moving carried furniture was baked as ground, or the actual street bin was omitted");
        }

        static void PooledPropScale()
        {
            using var fixture = new Fixture();
            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prop.transform.SetParent(fixture.Root.transform, false);
            prop.name = "SM_Prop_FittedDisplay";
            var original = PropFootprint.Of(prop);
            prop.transform.localScale = Vector3.one * .5f;
            var fitted = PropFootprint.Of(prop);
            Require(Mathf.Abs(fitted.Half.x - original.Half.x * .5f) < .001f,
                "a pooled prop kept the previous opening's collision dimensions");
            fixture.Root.transform.localScale = Vector3.one * 2f;
            var parentScaled = PropFootprint.Of(prop);
            Require(Mathf.Abs(parentScaled.Half.x - original.Half.x) < .001f,
                "composed furniture ignored its parent's physical scale");
            prop.transform.localScale = Vector3.one;
            Require(Mathf.Abs(PropFootprint.Of(prop).Half.x - original.Half.x * 2f) < .001f,
                "restoring the pooled prefab retained its shrunken footprint");
        }

        static void AutomaticCadence()
        {
            var random = UnityEngine.Random.state;
            int Measure(float dt)
            {
                using var fixture = new Fixture();
                var man = fixture.Man();
                var target = new GameObject("Cadence car");
                target.transform.SetParent(fixture.Root.transform);
                target.transform.position = man.Tf.position + Vector3.forward;
                var car = new RoadCar { Tf = target.transform };
                var gun = new GameObject("Cadence gun");
                gun.transform.SetParent(fixture.Root.transform);
                gun.transform.position = man.Tf.position + Vector3.up * .9f;
                try
                {
                    man.Arm(gun, EquipmentKind.TommyGun);
                    Write(man, "<Weapon>k__BackingField", gun.transform);
                    man.ShootUp(car);
                    Write(man, "_fireTimer", .14f);
                    int shots = 0;
                    man.Fired = _ => shots++;
                    int steps = Mathf.RoundToInt(5.6f / dt);
                    for (int i = 0; i < steps; i++)
                    {
                        Write(man, "_aimBlend", 1f);
                        Call(man, "TickShootUp", dt);
                    }
                    return shots;
                }
                finally { RetireCar(car); man.Dispose(); }
            }
            try
            {
                int fine = Measure(.05f), coarse = Measure(.8f);
                Require(fine == 40 && coarse == fine,
                    $"5.6 seconds of Tommy fire: fine={fine}, coarse={coarse}, expected 40 each");
            }
            finally { UnityEngine.Random.state = random; }
        }

        static void BlockedGunCadence()
        {
            float timer = .14f;
            Require(GunCadence.Advance(ref timer, 80f, .14f, false).Count == 0,
                "blocked aim discharged a round");
            var due = GunCadence.Advance(ref timer, .05f, .14f);
            Require(due.Count == 1 && Mathf.Abs(timer - .09f) < .0001f,
                "raising the gun released a backlog from the time it was blocked");
            due = GunCadence.Advance(ref timer, .8f, .14f);
            Require(due.Count == 6 && Mathf.Abs(due.First - .09f) < .0001f &&
                Mathf.Abs(timer - .13f) < .0001f,
                "continuous trigger lost the fractional cooldown between frames");
        }

        static void InterleavedRounds()
        {
            using var fixture = new Fixture();
            var arena = fixture.Root.AddComponent<DemoCrews>();
            var a = fixture.Man(); var b = fixture.Man();
            b.Tf.position += Vector3.right * 10f;
            foreach (var man in new[] { a, b })
            {
                var gun = new GameObject("Round fixture gun");
                gun.transform.SetParent(fixture.Root.transform);
                gun.transform.position = man.Tf.position + Vector3.up;
                man.Arm(gun, EquipmentKind.Pistol);
                Write(man, "<Weapon>k__BackingField", gun.transform);
            }
            var heard = new List<CrewWalker>();
            void Hear(StreetAlarm.Shot shot)
            {
                if (shot.Shooter != a && shot.Shooter != b) return;
                heard.Add(shot.Shooter);
                if (shot.Shooter == a) a.Surrendered = true;
            }
            var random = UnityEngine.Random.state;
            StreetAlarm.OnShot += Hear;
            try
            {
                Call(arena, "BeginRounds");
                void Queue(CrewWalker man, float at) => Call(arena, "QueueRound", man, null,
                    man.MuzzlePosition, man.Tf.position, man.Tf, at, false, false);
                Queue(a, .4f); Queue(a, .6f); Queue(b, .2f); Queue(b, .5f);
                Require(heard.Count == 0, "first tick applied wounds before the other shooter ticked");
                Call(arena, "FlushRounds");
                Require(heard.Count == 3 && heard[0] == b && heard[1] == a && heard[2] == b,
                    "rounds followed unit order or a surrendering shooter kept his future round");
            }
            finally
            {
                StreetAlarm.OnShot -= Hear;
                UnityEngine.Random.state = random;
                a.Dispose(); b.Dispose();
            }
        }

        static void CivilianGraphSpawn()
        {
            using var fixture = new Fixture();
            var centre = fixture.Root.transform.position + new Vector3(10000f, 0f, 10000f);
            var building = new SidewalkPlan();
            building.Take(new SidewalkPlan.Box { C = new Vector2(centre.x, centre.z),
                H = new Vector2(5f, 8f), Ax = Vector2.right, Az = Vector2.up, Solid = true });
            WalkObstacles.RegisterPlan(building);
            var body = new GameObject("Civilian spawn");
            body.transform.SetParent(fixture.Root.transform);
            var man = new PedestrianAgent();
            var random = UnityEngine.Random.state;
            try
            {
                var link = new PedLink { From = new PedNode { Pos = centre + Vector3.back * 12f },
                    To = new PedNode { Pos = centre + Vector3.forward * 12f }, Length = 24f };
                Require(man.Init(body.transform, new PedClips(), link, 12f) &&
                    !WalkObstacles.Standing(body.transform.position, WalkObstacles.Radius),
                    "ordinary civilian Init seated him in the middle of the structure");
                Require((PedLink)Read(man, "_link") == link && Mathf.Abs((float)Read(man, "_t") - 12f) > 8f,
                    "the valid initial seat lost its walkable graph connection");
                man.Dispose();
                var sealedLink = new PedLink { From = new PedNode { Pos = centre },
                    To = new PedNode { Pos = centre + Vector3.forward }, Length = 1f };
                Require(!man.Init(body.transform, new PedClips(), sealedLink, .5f),
                    "a completely sealed spawn reported success");
                var neighbour = new PedLink { From = sealedLink.To,
                    To = new PedNode { Pos = centre + Vector3.forward * 12f }, Length = 11f };
                sealedLink.To.Links.Add(neighbour);
                Require(man.Init(body.transform, new PedClips(), sealedLink, .5f) &&
                    (PedLink)Read(man, "_link") == neighbour && !WalkObstacles.Standing(body.transform.position, WalkObstacles.Radius),
                    "a sealed initial edge did not try its connected clear neighbour");
            }
            finally { man.Dispose(); WalkObstacles.UnregisterPlan(building); UnityEngine.Random.state = random; }
        }

        static void BlockedGraphSpawn()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var centre = man.Tf.position;
            var building = new SidewalkPlan();
            building.Take(new SidewalkPlan.Box { C = new Vector2(centre.x, centre.z),
                H = new Vector2(5f, 8f), Ax = Vector2.right, Az = Vector2.up, Solid = true });
            WalkObstacles.RegisterPlan(building);
            try
            {
                var link = new PedLink { From = new PedNode { Pos = centre + new Vector3(4f, 0f, -11f) },
                    To = new PedNode { Pos = centre + new Vector3(-6f, 0f, 11f) }, Length = 24.166f };
                typeof(PedestrianAgent).GetField("_link", Private).SetValue(man, link);
                Require(man.OnGraph && WalkObstacles.Standing(centre, WalkRoute.ClearanceRadius),
                    "fixture did not seat a body on a graph segment inside the building");
                Require((bool)Call(man, "TryClearSpawn") &&
                    !WalkObstacles.Standing(man.Tf.position, WalkRoute.ClearanceRadius) && !man.OnGraph,
                    "new body retained the obstructed graph seat");
                var route = new List<Vector3>();
                Require(WalkRoute.Plan(man.Tf.position, centre + Vector3.forward * 12f, route),
                    "repaired spawn still could not receive a route onto the street");
            }
            finally { WalkObstacles.UnregisterPlan(building); }
        }

        static void RecruitBesideIndoorLeader()
        {
            using var fixture = new Fixture();
            var boss = fixture.Man();
            var centre = boss.Tf.position;
            var outside = centre + Vector3.forward * 12f;
            var unit = fixture.Unit(boss);
            DoorBeat.RestoreInside(boss, outside);
            boss.Tf.position = centre;
            var building = new SidewalkPlan();
            building.Take(new SidewalkPlan.Box { C = new Vector2(centre.x, centre.z),
                H = new Vector2(8f, 8f), Ax = Vector2.right, Az = Vector2.up, Solid = true });
            WalkObstacles.RegisterPlan(building);
            try
            {
                var at = (Vector3)typeof(DemoCrews).GetMethod("RecruitPosition", StaticPrivate)
                    .Invoke(null, new object[] { unit, 0 });
                Require(at == outside && !WalkObstacles.Standing(at, WalkRoute.ClearanceRadius),
                    "the recruit was placed beside a hidden body inside the structure");
                var hood = fixture.Man();
                hood.Tf.position = at;
                unit.Hoods.Add(hood);
                var crews = fixture.Root.AddComponent<DemoCrews>();
                Call(crews, "FallIn", unit, hood, 0);
                Require(!hood.HasOrder, "the new hood was ordered back toward the hidden leader");

                var quarters = typeof(CrewQuarters);
                var key = quarters.GetMethod("Key", StaticPrivate).Invoke(null, new object[] { unit });
                var billet = Activator.CreateInstance(quarters.GetNestedType("Billet", BindingFlags.NonPublic), true);
                billet.GetType().GetField("Unit").SetValue(billet, unit);
                billet.GetType().GetField("Doorstep").SetValue(billet, outside);
                billet.GetType().GetField("In").SetValue(billet, true);
                ((IDictionary)quarters.GetField("Billets", StaticPrivate).GetValue(null)).Add(key, billet);
                CrewQuarters.Tick(crews);
                Require(DoorBeat.Active(hood) && DoorBeat.Held(boss),
                    "the newcomer did not start the ordinary entry while his lieutenant stayed inside");
            }
            finally { WalkObstacles.UnregisterPlan(building); }
        }

        static void ParkedPatrolAtGate()
        {
            var net = new LaneNet();
            var node = net.AddNode(10000f, 10000f, 6f, 6f);
            var road = net.AddRoad(new Vector3(10000f, 0f, 10006f),
                new Vector3(10000f, 0f, 10106f), 8f, new[] { 2.5f }, 10f, node, null, true);
            var feed = net.AddRoad(new Vector3(10000f, 0f, 9894f),
                new Vector3(10000f, 0f, 9994f), 8f, new[] { 2.5f }, 10f, null, node, true);
            var lane = road.LaneFor(1, 2.5f);
            var patrol = new RoadCar { Net = net, HalfLen = 3f, HalfWide = 1.2f };
            var joining = new RoadCar { HalfLen = 3f, HalfWide = 1.2f };
            try
            {
                patrol.PlaceAt(road.Pose(22.5f, 6.6f), road.Axis);
                Require(patrol.Parked, "fixture patrol was not parked at the kerb");
                Require(LaneGate.Clear(lane, 45f, 9f, joining, 21f, 10f),
                    "a parked patrol twenty-two metres behind reserved ninety metres of moving traffic");
                patrol.PlaceAt(road.Pose(45f, 6.6f), road.Axis);
                Require(LaneGate.Clear(lane, 45f, 9f, joining),
                    "a parked body beside the join blocked a physically clear lane");
                patrol.PlaceAt(road.Pose(45f, 3.5f), road.Axis);
                Require(!LaneGate.Clear(lane, 45f, 9f, joining),
                    "a parked body across the join was ignored");
                patrol.PlaceAt(road.Pose(22.5f, 2.5f), road.Axis);
                Require(!patrol.Parked && !LaneGate.Clear(lane, 45f, 9f, joining),
                    "stopped traffic lost its reservation to accelerate into the join");
                patrol.PlaceAt(feed.Pose(70f, 6.6f), feed.Axis);
                Require(LaneGate.Clear(lane, 1f, 9f, joining) && LaneGate.BoxClear(node, 9f, joining),
                    "a parked upstream car reserved the junction and exit");
                patrol.PlaceAt(feed.Pose(70f, 2.5f), feed.Axis);
                Require(!LaneGate.Clear(lane, 1f, 9f, joining) && !LaneGate.BoxClear(node, 9f, joining),
                    "upstream queued traffic no longer protected the junction and exit");
            }
            finally { RetireCar(patrol); RetireCar(joining); }
        }

        static void JailedLeaderCannotMarch()
        {
            var roster = LivingCity.Personnel.RosterSeeder.Generate(19, 8);
            var view = new LivingCity.Outfit.HouseView { Roster = roster };
            var candidate = typeof(LivingCity.Outfit.HouseMind).GetMethod("Candidate", StaticPrivate);
            LivingCity.Personnel.Crew crew = null;
            foreach (var row in roster.Crews)
                if ((bool)candidate.Invoke(null, new object[] { view, row })) { crew = row; break; }
            Require(crew != null, "fixture had no available rival street crew");
            var lieutenant = roster.Find(crew.LieutenantId);
            lieutenant.Status = LivingCity.Personnel.CharacterStatus.Jailed;
            Require(!(bool)candidate.Invoke(null, new object[] { view, crew }),
                "active hoods made their jailed lieutenant's nonexistent tactical group eligible");
            lieutenant.Status = LivingCity.Personnel.CharacterStatus.Active;
            Require((bool)candidate.Invoke(null, new object[] { view, crew }),
                "the released lieutenant's crew stayed unavailable");
        }

        static void GraphRecoveryPole()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var from = man.Tf.position;
            var goal = from + new Vector3(74.85f, 0f, .36f);
            var link = new PedLink { From = new PedNode { Pos = from }, To = new PedNode { Pos = goal }, Length = 74.85f };
            Write(man, "_destFwd", link);
            Write(man, "_destT", link.Length);
            Write(man, "<State>k__BackingField", CrewWalker.Mode.Homing);
            var plan = new SidewalkPlan();
            plan.Take(new SidewalkPlan.Box { C = new Vector2(from.x + .45f, from.z - .07f),
                H = new Vector2(.17f, .17f), Ax = Vector2.left, Az = Vector2.down, Solid = true });
            WalkObstacles.RegisterPlan(plan);
            try
            {
                Require(!WalkObstacles.Standing(from, WalkRoute.ClearanceRadius) &&
                    WalkObstacles.Standing(from, WalkObstacles.Radius), "pole fixture missed the graph clearance shell");
                Call(man, "GraphStepBlocked", goal);
                Require(man.HasOrder && (man.Tf.position - from).sqrMagnitude > .001f &&
                    !WalkObstacles.BlocksStanding(from, man.Tf.position, WalkRoute.ClearanceRadius),
                    "graph overlap recovery crossed the pole or abandoned its onward order");
            }
            finally { WalkObstacles.UnregisterPlan(plan); }
        }

        static void PoliceReinforcements()
        {
            using var fixture = new Fixture();
            var crew = fixture.Unit(fixture.Man());
            var police = fixture.Unit(fixture.Man());
            police.Faction = StreetAlarm.PoliceFaction;
            bool MayAnswer(bool aboard) => (bool)typeof(DemoCrews)
                .GetMethod("MayAnswerShot", StaticPrivate).Invoke(null, new object[] { crew, police, aboard });
            var incident = typeof(StreetAlarm).GetField("<IncidentNumber>k__BackingField", StaticPrivate);
            var previous = incident.GetValue(null);
            try
            {
                incident.SetValue(null, 123);
                Require(!MayAnswer(false), "quiet suspect opened a police fight");
                crew.PoliceFightIncident = 123;
                crew.TargetUnit = null; // First patrol has died and EndSearch cleared it.
                Require(MayAnswer(false), "armed crew ignored shooting reinforcements");
                Require(!LivingCity.Police.PoliceProcedure.IsDefensivePoliceReturn(
                    crew.PoliceAttackedIncident, 123), "attacking police acquired a defensive exemption");
                crew.OrderedAt = Time.time;
                Require(!MayAnswer(false), "return fire overrode a fresh move order");
                crew.OrderedAt = -100f;
                crew.Fleeing = true;
                Require(!MayAnswer(false), "reinforcements cancelled flight");
                crew.Fleeing = false;
                crew.Surrendered = true;
                Require(!MayAnswer(true), "a surrendered rider resumed shooting");
                crew.Surrendered = false;
                incident.SetValue(null, 124);
                Require(!MayAnswer(false), "a previous incident reopened combat");
            }
            finally { incident.SetValue(null, previous); }
        }

        static void OrderedFlightSprints()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var unit = fixture.Unit(man);
            var crews = fixture.Root.AddComponent<DemoCrews>();
            var graph = UnityEngine.Playables.PlayableGraph.Create("Flight gait test");
            var clip = new AnimationClip();
            try
            {
                var poses = (UnityEngine.Animations.AnimationClipPlayable[])typeof(PedestrianAgent)
                    .GetField("_poses", Private).GetValue(man);
                poses[12] = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
                poses[15] = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
                Require(crews.OrderFlee(unit, man.Tf.position - Vector3.right * 5f) &&
                    (bool)Read(man, "_sprinting"), "RUN from the police remained an ordinary jog");
                Require(crews.OrderUnit(unit, man.Tf.position + Vector3.forward * 50f, out _) &&
                    (bool)Read(man, "_sprinting"), "redirecting flight lost the escape gait");
                crews.EndFlight(unit);
                Require(!(bool)Read(man, "_sprinting"), "ending flight retained its escape gait");
                Require(crews.OrderFlee(unit, man.Tf.position - Vector3.right * 5f), "second flight refused");
                var enemy = fixture.Unit(fixture.Man());
                enemy.Faction = StreetAlarm.PoliceFaction;
                crews.Select(unit);
                Require(crews.OrderAttack(enemy) && !unit.Fleeing &&
                    !(bool)Read(man, "_sprinting") && unit.TargetUnit == enemy,
                    "attack order left the crew simultaneously fleeing from its target");
            }
            finally { graph.Destroy(); Object.DestroyImmediate(clip); }
        }

        static void EntryIsNotInside()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            DoorBeat.RestoreInside(man, man.Tf.position);
            var beat = (DoorBeat)typeof(DoorBeat).GetField("instance", StaticPrivate).GetValue(null);
            var calls = (IList)Read(beat, "calls");
            object call = null;
            foreach (var candidate in calls)
                if (candidate.GetType().GetField("Man").GetValue(candidate) == man) call = candidate;
            Require(call != null && DoorBeat.Held(man), "restored actual occupant was not held");
            var phase = call.GetType().GetField("Phase");
            call.GetType().GetField("Inside").SetValue(call, false);
            phase.SetValue(call, Enum.Parse(phase.FieldType, "Entering"));
            man.Tf.gameObject.SetActive(true);
            Require(DoorBeat.Active(man) && !DoorBeat.Held(man),
                "custody/hiding completed while the visible body was still crossing the threshold");
            phase.SetValue(call, Enum.Parse(phase.FieldType, "Inside"));
            man.Tf.gameObject.SetActive(false);
            Require(DoorBeat.Held(man), "completed physical entry did not admit the occupant");
        }

        static void DetouredCorner()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var from = man.Tf.position;
            var plan = new SidewalkPlan();
            plan.Take(new SidewalkPlan.Box { C = new Vector2(from.x, from.z + 1f),
                H = new Vector2(.28f, .28f), Ax = new Vector2(-.2f, .98f).normalized,
                Az = new Vector2(-.98f, -.2f).normalized, Solid = true });
            WalkObstacles.RegisterPlan(plan);
            float scale = Time.timeScale;
            try
            {
                Time.timeScale = 16f;
                typeof(PedestrianAgent).GetField("_hold", Private).SetValue(man, 1f);
                var target = from + Vector3.forward * 2f;
                Require(WalkObstacles.BlocksStanding(from, target, WalkRoute.ClearanceRadius),
                    "corner fixture must have a blocked direct chord");
                Call(man, "TickStride", 16f / 3f, target, 0f, false, false, false, false, true);
                Require((man.Tf.position - from).sqrMagnitude > 1f &&
                    !WalkObstacles.BlocksStanding(from, man.Tf.position, WalkRoute.ClearanceRadius),
                    "a clear sidelong stride snapped to the corner through the chair");
            }
            finally { Time.timeScale = scale; WalkObstacles.UnregisterPlan(plan); }
        }

        static void FlightSeeksCover()
        {
            using var fixture = new Fixture();
            var start = fixture.Man().Tf.position;
            var oldSolids = typeof(WalkObstacles).GetField("_solids", StaticPrivate).GetValue(null);
            var oldMin = WalkObstacles.Min;
            var oldMax = WalkObstacles.Max;
            try
            {
                typeof(WalkObstacles).GetField("_solids", StaticPrivate).SetValue(null, new SidewalkPlan());
                WalkObstacles.Block(start.x + 10f, start.x + 20f, start.z - 5f, start.z + 25f);
                var threat = start - Vector3.right * 5f;
                Require(FlightRoute.TryGoal(start, threat, out var goal) &&
                    !WalkObstacles.Sees(threat, goal), "flight ignored a reachable building that breaks sight");
                var way = new List<Vector3>();
                Require(WalkRoute.Plan(start, goal, way), "flight chose unreachable cover");
                var edge = start + Vector3.right * 549f;
                Require(FlightRoute.TryGoal(edge, edge - Vector3.right * 5f, out goal) &&
                    WalkObstacles.InCity(goal) && Vector3.Distance(edge, goal) >= 12f,
                    "flight ran out of movement at the city fence");
            }
            finally
            {
                typeof(WalkObstacles).GetField("_solids", StaticPrivate).SetValue(null, oldSolids);
                WalkObstacles.Min = oldMin; WalkObstacles.Max = oldMax; WalkObstacles.Version++;
            }
        }

        static void FlightContinues()
        {
            using var fixture = new Fixture();
            var crews = fixture.Root.AddComponent<DemoCrews>();
            var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
            Write(dispatch, "_crews", crews);
            var man = fixture.Man();
            var unit = fixture.Unit(man);
            unit.Fleeing = true;
            unit.FlightFrom = man.Tf.position - Vector3.right * 70f;
            unit.FledAt = Time.time - 30f;
            unit.SeenByLawAt = Time.time - 3f;
            float fled = unit.FledAt, seen = unit.SeenByLawAt;
            Call(dispatch, "TickFlight", unit, null, 1, null);
            Require(man.HasOrder && man.Urgent &&
                man.OrderDestination.x > man.Tf.position.x + 60f,
                "a runner waited at the end of his leg during pursuit");
            Require(unit.FledAt == fled && unit.SeenByLawAt == seen && unit.Fleeing,
                "continuing the run restarted its history or sighting clock");
            var goal = man.OrderDestination;
            Require(!crews.ContinueFlight(unit, man.Tf.position + Vector3.right * 20f) &&
                man.OrderDestination == goal, "continuation overwrote an existing player move");
        }

        static void AcceleratedWalkingPace()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var origin = man.Tf.position;
            float previousScale = Time.timeScale;
            var hold = typeof(PedestrianAgent).GetMethod("HoldStep", Private);
            try
            {
                foreach (float scale in new[] { 1f, 4f, 16f })
                {
                    Time.timeScale = scale;
                    float elapsed = scale / 30f;
                    var wanted = origin + Vector3.right * (3f * elapsed);
                    var actual = (Vector3)hold.Invoke(man, new object[] { wanted, elapsed, 3f });
                    Require(Mathf.Abs((actual.x - origin.x) / elapsed - 3f) < 0.02f,
                        "frame ceiling slowed the walker at " + scale + "x");
                }
                Time.timeScale = 16f;
                typeof(PedestrianAgent).GetField("_hold", Private).SetValue(man, 1f);
                Call(man, "TickStride", 16f / 3f, origin + Vector3.right * 30f,
                    0f, false, false, false, false, true);
                Require(man.Tf.position.x - origin.x > 6f,
                    "fixed lookahead throttled an accelerated clear-ground stride");
            }
            finally { Time.timeScale = previousScale; }
        }

        static void SidestepClearsWholeSegment()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var centre = man.Tf.position;
            var from = centre + new Vector3(-0.35f, 0f, -0.59f);
            var to = from + Vector3.right * 0.7f;
            var plan = new SidewalkPlan();
            plan.Take(SidewalkPlan.Make(new Vector2(centre.x, centre.z), 0f,
                new Vector2(0.17f, 0.17f), true));
            WalkObstacles.RegisterPlan(plan);
            try
            {
                Require(!WalkObstacles.Occupied(from, WalkObstacles.Radius) &&
                    !WalkObstacles.Occupied(to, WalkObstacles.Radius), "sidestep endpoints must both be free");
                Require(WalkObstacles.BlocksStanding(from, to, WalkObstacles.Radius),
                    "sidestep fixture did not cross the pole's shoulder clearance");
                man.Tf.position = from;
                var type = typeof(PedestrianAgent);
                var join = type.GetField("_join", Private);
                join.SetValue(man, Enum.Parse(join.FieldType, "Stepping"));
                type.GetField("_stepAsked", Private).SetValue(man, Time.frameCount);
                type.GetField("_stepPace", Private).SetValue(man, 0.7f);
                type.GetField("_stepDir", Private).SetValue(man, Vector3.right);
                type.GetField("_joinLeft", Private).SetValue(man, 10f);
                type.GetMethod("SpendJoin", Private).Invoke(man, new object[] { 1f });
                Require(man.Tf.position == from, "sidestep accepted free endpoints across a solid prop");
                Call(man, "Nudge", Vector3.right, 0.7f);
                Require(man.Tf.position == from, "moving crowd nudge crossed a solid prop");
            }
            finally { WalkObstacles.UnregisterPlan(plan); }
        }

        static void ShortStreetResponseBraking()
        {
            using var fixture = new Fixture();
            foreach (float frameTime in new[] { 1f / 30f, 16f / 30f })
            {
                var net = new LaneNet();
                var a = net.AddNode(10000f, 10000f, 5f, 5f, 5.7f);
                var b = net.AddNode(10100f, 10000f, 5f, 5f, 5.7f);
                var c = net.AddNode(10145f, 10000f, 5f, 5f, 5.7f);
                var d = net.AddNode(10245f, 10000f, 5f, 5f, 5.7f);
                Carriageway Join(RoadNode start, RoadNode end) => net.AddRoad(
                    new Vector3(start.XMax, 0f, start.Z), new Vector3(end.XMin, 0f, end.Z),
                    5f, new[] { 2.5f }, 10f, start, end, false);
                var ab = Join(a, b); var bc = Join(b, c); var cd = Join(c, d);
                net.Finish();
                var blocker = new RoadCar { Net = net, Tf = fixture.Man().Tf, HalfLen = 3f };
                var car = new RoadCar { Net = net, Tf = fixture.Man().Tf, Profile = DriverProfile.Police,
                    HalfLen = 3.723523f, HalfWide = 1.28418f };
                try
                {
                    blocker.Spawn(cd.LaneFor(1, 2.5f), 4f);
                    blocker.Halt(true);
                    car.Spawn(ab.LaneFor(1, 2.5f), 10f);
                    Require(car.GoTo(cd.Pose(70f, 2.5f), false, wantHeading: 1), "short street route refused");
                    bool reached = false;
                    float closestNose = float.MaxValue;
                    for (int i = 0; i < Mathf.CeilToInt(60f / frameTime); i++)
                    {
                        if (car.Road == bc) { car.Profile = DriverProfile.Patrol; reached = true; }
                        car.Tick(frameTime);
                        if (car.Road == bc)
                            closestNose = Mathf.Min(closestNose, bc.Length - car.S - car.HalfLen);
                        Require(car.Road != cd && (car.Via == null || car.Via.Node != c),
                            "responder entered the occupied crossing after its mission ended");
                    }
                    Require(reached && closestNose >= c.StopSetback - 0.35f,
                        "short exit left too little braking room: nose setback " + closestNose);
                }
                finally { RetireCar(car); RetireCar(blocker); }
            }
        }

        static void WalkAroundParkedSedan()
        {
            var city = new Rect(9980f, 9980f, 40f, 40f);
            WalkObstacles.City.Add(city);
            var body = SidewalkPlan.Make(new Vector2(10000f, 10000f), 90f,
                new Vector2(1.25f, 3.1f), true);
            var obstacle = new SidewalkPlan();
            obstacle.Take(body);
            var route = new List<Vector3>();
            var planner = typeof(CrewWalker).Assembly.GetType("RoadDemo.ParkedCarWalkRoute")
                .GetMethod("PlanAround", StaticPrivate);
            try
            {
                foreach (float x in new[] { -0.1f, 0.1f, -2.5f, 2.5f })
                foreach (float direction in new[] { -1f, 1f })
                {
                    var from = new Vector3(10000f + x, 0f, 10000f - 2.1f * direction);
                    var to = new Vector3(10000f + x, 0f, 10000f + 3.5f * direction);
                    Require((bool)planner.Invoke(null, new object[] { from, to, new[] { body }, route }),
                        "no detour from the sedan door at " + from);
                    Require(route.Count >= 3 && route[route.Count - 1] == to,
                        "detour did not retain the destination and both body corners");
                    var previous = from;
                    foreach (var corner in route)
                    {
                        Require(!obstacle.Obstructs(new Vector2(previous.x, previous.z),
                            new Vector2(corner.x, corner.z), WalkObstacles.Radius),
                            "walking chord cut through the parked body");
                        previous = corner;
                    }
                }
                Require(!(bool)planner.Invoke(null, new object[] {
                    new Vector3(10000f, 0f, 9997f), new Vector3(10000f, 0f, 10000f),
                    new[] { body }, route }), "accepted a destination inside the parked car");
            }
            finally { WalkObstacles.City.Remove(city); }
        }

        static void BagReachesHeadquarters()
        {
            using var fixture = new Fixture();
            var runtime = fixture.Root.AddComponent<TerritoryRuntime>();
            var front = fixture.Root.AddComponent<GangFront>();
            front.GangId = 0;
            front.Entry = new Vector3(10000f, 0f, 10000f);
            front.EntryLink = new PedLink();
            front.Door = front.Entry + Vector3.forward * 6f;
            var fronts = (IList)typeof(GangFront).GetField("all", StaticPrivate).GetValue(null);
            if (!fronts.Contains(front)) fronts.Add(front);
            try
            {
                var collector = fixture.Man();
                collector.CharacterId = 77;
                collector.Tf.position = front.Entry + Vector3.right * 14f;
                var unit = fixture.Unit(collector);
                unit.CrewId = 12;
                var round = new LivingCity.Territory.TerritoryRound {
                    House = LivingCity.Gameplay.PlayerCommands.House, CrewId = 12,
                    CollectorId = 77, Carried = 111,
                    Stage = LivingCity.Territory.TerritoryRoundStage.HeadingHome };
                var ledger = new LivingCity.Territory.TerritoryRoundLedger(null, null);
                int banked = 0;
                ledger.Ended += ended => banked += ended.Carried;
                Write(runtime, "roundLedger", ledger);
                var bodyType = typeof(TerritoryRuntime).GetNestedType("RoundBody", BindingFlags.NonPublic);
                var body = Activator.CreateInstance(bodyType, true);
                bodyType.GetField("Round").SetValue(body, round);
                bodyType.GetField("Collector").SetValue(body, collector);
                bodyType.GetField("Walkers").SetValue(body, unit);
                ((IList)Read(runtime, "bodies")).Add(body);
                Call(runtime, "NoteRoundArrival", unit, collector,
                    default(LivingCity.Territory.TerritoryActorObservation), 1d);
                Require(banked == 0 && round.Stage == LivingCity.Territory.TerritoryRoundStage.HeadingHome,
                    "money banked while the collector was fourteen metres from headquarters");
                collector.Tf.position = front.Entry;
                Call(runtime, "NoteRoundArrival", unit, collector,
                    default(LivingCity.Territory.TerritoryActorObservation), 2d);
                Require(banked == 111 && round.Stage == LivingCity.Territory.TerritoryRoundStage.Banked,
                    "collector could not bank at the actual pavement approach");
            }
            finally { fronts.Remove(front); }
        }

        static void OffCentreJunctionEntry()
        {
            using var fixture = new Fixture();
            var net = new LaneNet();
            var a = net.AddNode(10000f, 10000f, 5f, 5f, 5.7f);
            var b = net.AddNode(10100f, 10000f, 5f, 5f, 5.7f);
            var c = net.AddNode(10200f, 10000f, 5f, 5f, 5.7f);
            var ab = net.AddRoad(new Vector3(a.XMax, 0f, a.Z), new Vector3(b.XMin, 0f, b.Z),
                5f, new[] { 2.5f }, 10f, a, b, false);
            var bc = net.AddRoad(new Vector3(b.XMax, 0f, b.Z), new Vector3(c.XMin, 0f, c.Z),
                5f, new[] { 2.5f }, 10f, b, c, false);
            net.Finish();
            for (int heading = -1; heading <= 1; heading += 2)
            {
                var car = new RoadCar { Net = net, Tf = fixture.Man().Tf, Profile = DriverProfile.Traffic };
                try
                {
                    var lane = (heading > 0 ? ab : bc).LaneFor(heading, heading * 2.5f);
                    car.Spawn(lane, lane.Length);
                    Write(car, "<D>k__BackingField", lane.Offset + 2.5f);
                    Call(car, "PlanNext", b);
                    var before = new object[] { Vector3.zero, Vector3.zero };
                    typeof(RoadCar).GetMethod("Pose", Private).Invoke(car, before);
                    Call(car, "EnterNode", b, 0f);
                    var after = new object[] { Vector3.zero, Vector3.zero };
                    typeof(RoadCar).GetMethod("Pose", Private).Invoke(car, after);
                    Require(Vector3.Distance((Vector3)before[0], (Vector3)after[0]) < 0.02f,
                        "junction entry snapped an off-centre car laterally for heading " + heading);
                }
                finally { RetireCar(car); }
            }
        }

        static void CustodyDoesNotTether()
        {
            using var fixture = new Fixture();
            var crews = fixture.Root.AddComponent<DemoCrews>();
            var boss = fixture.Man();
            var hood = fixture.Man();
            hood.Tf.position = boss.Tf.position + Vector3.right * 20f;
            var door = hood.Tf.position + Vector3.right * 25f;
            var unit = fixture.Unit(boss);
            unit.Hoods.Add(hood);
            crews.Units.Add(unit);
            for (int stage = 0; stage < 3; stage++)
            {
                unit.InCustody = stage == 0;
                hood.Surrendered = stage == 1;
                unit.Fleeing = stage == 2;
                hood.OrderToPoint(door);
                Call(crews, "TickCohesion");
                Require(hood.HasOrder && Vector3.Distance(hood.OrderDestination, door) < 0.1f,
                    "crew cohesion replaced a prisoner's walk to the police car");
            }
        }

        static void ParkingDepartureFairness()
        {
            using var fixture = new Fixture();
            var net = new LaneNet();
            net.AddRoad(new Vector3(9990f, 0f, 9990f), new Vector3(10050f, 0f, 9990f),
                8f, new[] { 2.5f }, 10f, null, null, false);
            fixture.Root.transform.position = new Vector3(10000f, 0f, 10000f);
            var site = (ParkingBlockSite)Activator.CreateInstance(typeof(ParkingBlockSite),
                Private, null, new object[] { ParkingBlockPlan.Generate(30f, 15f), fixture.Root.transform,
                    new Rect(), ParkingEntrySide.South, ParkingBlockStyle.Attended, null, null }, null);
            var lot = new ParkingLot(site, net, 0, 1987, fixture.Root.transform);
            var first = new ParkingCar();
            var waiting = new ParkingCar();
            var returning = new ParkingCar();
            try
            {
                Require((bool)Call(lot, "TryUseDrive", first), "first departure did not receive its turn");
                Require(!(bool)Call(lot, "TryUseDrive", waiting), "second departure ignored the current owner");
                Require(!(bool)Call(lot, "RequestReturn", returning), "returner ignored the current owner");
                Call(lot, "ReleaseDrive", first);
                Require(!(bool)Call(lot, "RequestReturn", returning), "returner jumped an older departure");
                Require(!(bool)Call(lot, "TryUseDrive", first), "first bay monopolised the next departure");
                Require((bool)Call(lot, "TryUseDrive", waiting), "older bay never received its departure");
                Call(lot, "ReleaseDrive", waiting);
                Require((bool)Call(lot, "RequestReturn", returning), "FIFO failed to admit the next return");
            }
            finally { first.Vanish(); waiting.Vanish(); returning.Vanish(); lot.Dispose(); }
        }

        static void RetiredCustodyMember()
        {
            using var fixture = new Fixture();
            var crews = fixture.Root.AddComponent<DemoCrews>();
            var boss = fixture.Man();
            var hood = fixture.Man();
            hood.CharacterId = 3;
            var unit = fixture.Unit(boss);
            unit.Hoods.Add(hood);
            unit.InCustody = unit.CustodyTracked = true;
            crews.Units.Add(unit);
            ((IDictionary)Read(crews, "_byCharacter")).Add(3, hood);
            Object.DestroyImmediate(hood.Tf.gameObject);
            Call(crews, "RemoveMan", 3);
            Require(!unit.Hoods.Contains(hood) && crews.BodyOf(3) == null,
                "retired hood still belonged to a commandable custody unit");
            Require(unit.Boss == boss && unit.InCustody,
                "retiring a hood changed the held lieutenant");
            Write(crews, "_seenVersion", 10);
            crews.ReleaseCustodyTracking(3, Vector3.zero, false);
            Require((int)Read(crews, "_seenVersion") == -1,
                "custody release left a previously projected roster unchanged");
        }

        static void BoardingApproachClearsCar()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var crews = fixture.Root.AddComponent<DemoCrews>();
                var man = fixture.Man();
                var origin = man.Tf.position;
                var turn = Quaternion.Euler(0, repetition * 72f, 0);
                var side = turn * Vector3.right;
                var forward = turn * Vector3.forward;
                man.Tf.position = origin + side * 8f;
                var door = origin - side * 2.18f + forward * .32f;
                var car = new RoadCar { Tf = fixture.Man().Tf, HalfLen = 3.72f, HalfWide = 1.284f };
                car.Tf.SetPositionAndRotation(origin, turn);
                car.PlaceAt(origin, forward);
                typeof(RoadCar).GetProperty("Parked").SetValue(car, true);
                StreetTraffic.Users.Add(car);
                var plan = new SidewalkPlan();
                plan.Take(new SidewalkPlan.Box { C = new Vector2(door.x, door.z), H = new Vector2(.1f, .4f),
                    Ax = new Vector2(side.x, side.z), Az = new Vector2(forward.x, forward.z), Solid = true });
                WalkObstacles.RegisterPlan(plan);
                try
                {
                    var before = man.Tf.position;
                    typeof(PrisonerCarriage).GetMethod("OrderBoarderToDoor", StaticPrivate)
                        .Invoke(null, new object[] { crews, man, door, false });
                    Require(man.HasOrder && !WalkObstacles.Occupied(man.OrderDestination, WalkObstacles.Radius),
                        "door adjustment chose a point inside the parked car clearance");
                    Require((man.OrderDestination - door).magnitude <= 2.01f && man.Tf.position == before,
                        "boarding adjustment changed the physical entry radius or teleported the boarder");
                    var route = new List<Vector3>();
                    var planner = typeof(CrewWalker).Assembly.GetType("RoadDemo.ParkedCarWalkRoute");
                    Require((bool)planner.GetMethod("TryPlan").Invoke(null,
                        new object[] { before, man.OrderDestination, route }),
                        "parked-car planner could not reach the adjusted boarding approach");
                }
                finally { WalkObstacles.UnregisterPlan(plan); RetireCar(car); }
            }
        }

        static void NewBoardingCancelsOldWalk()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var crews = fixture.Root.AddComponent<DemoCrews>();
                var man = fixture.Man();
                var origin = man.Tf.position;
                var forward = Quaternion.Euler(0, repetition * 72f, 0) * Vector3.forward;
                var escort = fixture.Man();
                escort.Tf.position = origin - forward * 10f;
                var car = new RoadCar { Tf = fixture.Man().Tf };
                car.Tf.position = origin + forward * 20f;
                try
                {
                    man.OrderToPoint(origin + forward * 100f);
                    var boarding = new PrisonerCarriage.BoardingMan { Man = man, Escort = escort,
                        Car = car, Seat = 2, Prisoner = true };
                    Require(PrisonerCarriage.BeginPrisonerBoarding(boarding, crews), "pickup did not initialize");
                    Require((man.OrderDestination - origin).sqrMagnitude < .001f,
                        "new pickup retained an old foot march while waiting for the escort");
                    man.TickCrew(.5f);
                    Require((man.Tf.position - origin).sqrMagnitude < .001f && !boarding.Seated,
                        "unescorted prisoner walked away or was seated at a distance");
                    Require(escort.HasOrder, "stopping the prisoner failed to bring the escort to him");
                }
                finally { RetireCar(car); }
            }
        }

        static void RecomputedVehiclePose()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var turn = Quaternion.Euler(0, repetition * 72f, 0);
                var origin = new Vector3(10000f, 0f, 10000f);
                Vector3 Point(float x, float z) => origin + turn * new Vector3(x, 0, z);
                var net = new LaneNet();
                var road = net.AddRoad(Point(80, -200), Point(80, 200), 15f, new[] { 2.5f }, 10f, null, null, true);
                net.Finish();
                var car = new RoadCar { Net = net, Tf = fixture.Man().Tf, HalfLen = 3.72f, HalfWide = 1.28f };
                var other = new RoadCar { Net = net, Tf = fixture.Man().Tf, HalfLen = 3.72f, HalfWide = 1.28f };
                try
                {
                    car.Spawn(road.LaneFor(-1, -2.5f), 113.4f);
                    Write(car, "<D>k__BackingField", -4.9f);
                    var before = Point(75.5f, 86.96f);
                    var forward = turn * new Vector3(-.54f, 0, -.84f).normalized;
                    Write(car, "_pos", before); Write(car, "_fwd", forward);
                    other.Spawn(road.LaneFor(-1, -2.5f), 118.48f);
                    StreetTraffic.Users.Add(car); StreetTraffic.Users.Add(other);
                    Require(!RoadSpace.Overlap(car.Position, car.Forward, car.HalfLen, car.HalfWide,
                        other.Position, other.Forward, other.HalfLen, other.HalfWide, 0f, out _), "initial bodies overlap");
                    for (int frame = 0; frame < 10; frame++)
                    {
                        typeof(RoadSpace).GetMethod("Invalidate", StaticPrivate).Invoke(null, null);
                        Call(car, "Place", .033f, car.S, car.D);
                    }
                    Require(Vector3.Distance(car.Position, before) < .001f && Vector3.Angle(car.Forward, forward) < .01f,
                        "rejected placement still wrote the recomputed position or heading");
                    RetireCar(other);
                    typeof(RoadSpace).GetMethod("Invalidate", StaticPrivate).Invoke(null, null);
                    Call(car, "Place", .033f, car.S, car.D);
                    Require(Vector3.Angle(car.Forward, turn * Vector3.back) < .01f,
                        "pose guard never resumed after the blocking body left");
                }
                finally { RetireCar(car); RetireCar(other); }
            }
        }

        static void PendingHoodDoesNotRelockBailedLeader()
        {
            var previousCrews = DemoCrews.Active;
            var previousForce = PoliceForce.Instance;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var world = LivingCity.Outfit.Underworld.Deal(1987 + repetition, 2);
                    var roster = world.Of(1).Roster;
                    var crews = fixture.Root.AddComponent<DemoCrews>();
                    typeof(DemoCrews).GetProperty(nameof(DemoCrews.Active)).SetValue(null, crews);
                    Write(crews, "_root", fixture.Root.transform);
                    var force = fixture.Root.AddComponent<PoliceForce>();
                    typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance)).SetValue(null, force);
                    var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
                    dispatch.Force = force;
                    force.Init(dispatch);
                    var boss = fixture.Man(); boss.CharacterId = roster.Members[0].Id; boss.Faction = 1;
                    var hood = fixture.Man(); hood.CharacterId = roster.Members[1].Id; hood.Faction = 1;
                    var unit = fixture.Unit(boss); unit.Faction = 1; unit.Hoods.Add(hood);
                    crews.Units.Add(unit);
                    var bodies = (IDictionary)Read(crews, "_byCharacter");
                    bodies[boss.CharacterId] = boss; bodies[hood.CharacterId] = hood;
                    var type = typeof(PoliceDispatch).GetNestedType("Custody", BindingFlags.NonPublic);
                    var prisonerType = typeof(PoliceDispatch).GetNestedType("CustodyPrisoner", BindingFlags.NonPublic);
                    var custody = Activator.CreateInstance(type, true);
                    type.GetField("Crew").SetValue(custody, unit);
                    foreach (var man in new[] { boss, hood })
                    {
                        var entry = Activator.CreateInstance(prisonerType, true);
                        prisonerType.GetField("Man").SetValue(entry, man);
                        prisonerType.GetField("CharacterId").SetValue(entry, man.CharacterId);
                        prisonerType.GetField("Booked").SetValue(entry, man == boss);
                        ((IList)type.GetField("Prisoners").GetValue(custody)).Add(entry);
                    }
                    ((IList)Read(dispatch, "_custodies")).Add(custody);
                    var prisoner = force.Pipeline.Book(roster, boss.CharacterId, Deed.Affray, 1);
                    force.KeepCustodyAlive(boss.CharacterId);
                    Call(dispatch, "ReassertCustody", custody);
                    Require(unit.InCustody && unit.Surrendered && hood.Surrendered,
                        "custody did not retain the booked leader and pending hood");
                    Require(force.Pipeline.PostBail(roster, prisoner, 2000, 1), "fixture bail refused");
                    force.ReleaseCustodyTracking(boss.CharacterId, boss.Tf.position, relocate: false);
                    for (int frame = 0; frame < 3; frame++)
                    {
                        Call(dispatch, "ReassertCustody", custody);
                        Require(!unit.InCustody && !unit.Surrendered && !boss.Surrendered,
                            "pending hood reasserted custody over a bailed leader at repetition " + repetition);
                        Require(hood.Surrendered, "bailing the leader also freed his unbooked hood");
                    }
                    Write(boss, "<State>k__BackingField", CrewWalker.Mode.Dead);
                    Call(dispatch, "ReassertCustody", custody);
                    Require(unit.InCustody && unit.Surrendered,
                        "a fallen commander unlocked his pending hood");
                    unit.Boss = null;
                    Call(dispatch, "ReassertCustody", custody);
                    Require(unit.InCustody && unit.Surrendered,
                        "a detachment without a lieutenant lost its pending-member command lock");
                }
            }
            finally
            {
                typeof(DemoCrews).GetProperty(nameof(DemoCrews.Active)).SetValue(null, previousCrews);
                typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance)).SetValue(null, previousForce);
            }
        }


        static void BookingAfterBailKeepsLeaderFree()
        {
            var forceInstance = typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance));
            var previousForce = PoliceForce.Instance;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var crews = fixture.Root.AddComponent<DemoCrews>();
                    var force = fixture.Root.AddComponent<PoliceForce>();
                    var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
                    dispatch.Force = force;
                    Write(dispatch, "_crews", crews);
                    forceInstance.SetValue(null, force);
                    var boss = fixture.Man(); boss.CharacterId = 1000 + repetition * 2;
                    var hood = fixture.Man(); hood.CharacterId = boss.CharacterId + 1;
                    var unit = fixture.Unit(boss); unit.Faction = 1; unit.Hoods.Add(hood);
                    crews.Units.Add(unit);
                    force.KeepCustodyAlive(hood.CharacterId);
                    var type = typeof(PoliceDispatch).GetNestedType("Custody", BindingFlags.NonPublic);
                    var custody = Activator.CreateInstance(type, true);
                    type.GetField("Crew").SetValue(custody, unit);
                    Call(dispatch, "FinishBookedCustody", custody);
                    Require(!unit.InCustody && !unit.CustodyTracked && !unit.Surrendered,
                        "finishing the hood's booking re-locked an already released leader");
                    Require(force.KeepsCustodyAlive(hood.CharacterId) && !force.KeepsCustodyAlive(boss.CharacterId),
                        "finishing booking changed the separate prisoner pins");
                    force.KeepCustodyAlive(boss.CharacterId);
                    Call(dispatch, "FinishBookedCustody", custody);
                    Require(unit.InCustody && unit.CustodyTracked && unit.Surrendered,
                        "a genuinely held leader was unlocked when his booking finished");
                }
            }
            finally { forceInstance.SetValue(null, previousForce); }
        }

        static void ParkingReturnTolerance()
        {
            foreach (float overshoot in new[] { 1.583f, 2f, 2.5f, 2.9f, 3f })
            {
                using var fixture = new Fixture();
                var net = new LaneNet();
                net.AddRoad(new Vector3(10000f, 0f, 10000f), new Vector3(10100f, 0f, 10000f),
                    8f, new[] { 2.5f }, 10f, null, null, true);
                var plan = ParkingBlockPlan.Generate(40f, 30f);
                fixture.Root.transform.position = new Vector3(10030f, 0f, 10015f);
                var site = (ParkingBlockSite)Activator.CreateInstance(typeof(ParkingBlockSite),
                    Private, null, new object[] { plan, fixture.Root.transform, new Rect(),
                        ParkingEntrySide.South, ParkingBlockStyle.Attended, null, null }, null);
                var lot = new ParkingLot(site, net, 0, 1987, fixture.Root.transform);
                var car = new ParkingCar { Net = net, Tf = fixture.Man().Tf };
                try
                {
                    Call(car, "Bind", lot, plan.Stalls[0], 0, 1f,
                        Vector2.one * 20f, Vector2.one * 20f, new System.Random(1));
                    float join = (float)Read(lot, "_joinProgress");
                    car.Spawn(lot.HomeLane, join + 6f);
                    Write(car, "<State>k__BackingField", ParkingCar.Mode.Returning);
                    car.Halt(true);
                    Call(car, "TickReturn");
                    Require(car.HasGoal && !car.Halted && car.State == ParkingCar.Mode.Returning,
                        "distant halted returner neither replanned nor stayed on the road");
                    car.Spawn(lot.HomeLane, join + overshoot);
                    car.Halt(true);
                    typeof(RoadCar).GetField("<Speed>k__BackingField", Private).SetValue(car, 0f);
                    var before = car.Position;
                    Call(car, "TickReturn");
                    Require(car.State == ParkingCar.Mode.Entering, $"arrival {overshoot} metres past entrance stayed halted");
                    Require(Vector3.Distance(before, car.Position) < .001f,
                        "parking entrance teleported to hide its arrival error");
                    Require(((IList)Read(car, "_motions")).Count > 0,
                        "arrival skipped its physical entry sweep");
                }
                finally { RetireCar(car); lot.Dispose(); }
            }
        }

        static void ParkingAdmission()
        {
            using var fixture = new Fixture();
            var net = new LaneNet();
            var road = net.AddRoad(new Vector3(10000f, 0f, 10000f),
                new Vector3(10100f, 0f, 10000f), 8f, new[] { 2.5f }, 10f,
                null, null, true);
            var plan = ParkingBlockPlan.Generate(40f, 30f);
            fixture.Root.transform.position = new Vector3(10030f, 0f, 10015f);
            var site = (ParkingBlockSite)Activator.CreateInstance(typeof(ParkingBlockSite),
                Private, null, new object[] { plan, fixture.Root.transform, new Rect(),
                    ParkingEntrySide.South, ParkingBlockStyle.Attended, null, null }, null);
            var lot = new ParkingLot(site, net, 0, 1987, fixture.Root.transform);
            var cars = new List<ParkingCar>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var car = new ParkingCar { Net = net, Tf = fixture.Man().Tf };
                    Call(car, "Bind", lot, plan.Stalls[i], i, 1f,
                        new Vector2(16f, 30f), new Vector2(10f, 22f), new System.Random(i));
                    cars.Add(car);
                    ((IList)Read(lot, "_cars")).Add(car);
                }
                Require((bool)Call(lot, "TryUseDrive", cars[0]), "outbound car could not claim empty drive");
                for (int i = 1; i < 3; i++)
                {
                    cars[i].Spawn(lot.HomeLane, 5f + i * 10f);
                    Write(cars[i], "<State>k__BackingField", ParkingCar.Mode.Driving);
                    Call(cars[i], "BeginReturn");
                    Require(cars[i].State == ParkingCar.Mode.Driving && !cars[i].HasGoal && !cars[i].Halted,
                        "returner stopped on the road while an exit held the driveway");
                }
                Call(lot, "ReleaseDrive", cars[0]);
                Require(!(bool)Call(lot, "TryUseDrive", cars[0]), "another exit jumped waiting returns");
                Call(cars[2], "BeginReturn");
                Require(cars[2].State == ParkingCar.Mode.Driving, "newer request jumped the queue");
                Call(cars[1], "BeginReturn");
                for (int i = 0; i < 400 && cars[1].State == ParkingCar.Mode.Driving; i++)
                    Call(cars[1], "AdvancePlan");
                Require(cars[1].State == ParkingCar.Mode.Returning && cars[1].HasGoal,
                    "oldest return did not receive its route and driveway together");
                Require(ReferenceEquals(Read(lot, "_moving"), cars[1]), "return approach had no driveway owner");
                var parkingGoal = typeof(RoadCar).GetField("_goalPark", Private);
                Require(!(bool)parkingGoal.GetValue(cars[1]), "return goal still searches kerb slots across the gate");
                Object.DestroyImmediate(cars[1].Tf.gameObject);
                lot.Tick(1f / 30f);
                Require(!ReferenceEquals(Read(lot, "_moving"), cars[1]), "destroyed returner retained the driveway reservation");
            }
            finally { foreach (var car in cars) RetireCar(car); lot.Dispose(); }
        }

        static void InterruptedStationApproach()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var unit = fixture.Unit(man);
            var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
            var custodyType = typeof(PoliceDispatch).GetNestedType("Custody", BindingFlags.NonPublic);
            var prisonerType = typeof(PoliceDispatch).GetNestedType("CustodyPrisoner", BindingFlags.NonPublic);
            var custody = Activator.CreateInstance(custodyType, true);
            custodyType.GetField("Crew").SetValue(custody, unit);
            custodyType.GetField("Precinct").SetValue(custody, new PoliceForce.Precinct { Door = man.Tf.position + Vector3.right * 3f });
            var prisoner = Activator.CreateInstance(prisonerType, true);
            prisonerType.GetField("Man").SetValue(prisoner, man);
            prisonerType.GetField("InWave").SetValue(prisoner, true);
            ((IList)custodyType.GetField("Prisoners").GetValue(custody)).Add(prisoner);
            Require(!DoorBeat.Active(man), "fixture still had its original station walk");
            Require(!(bool)Call(dispatch, "TickStationThresholds", custody), "station booked a man before his threshold crossing");
            Require(DoorBeat.Active(man) && !DoorBeat.Held(man), "cancelled approach was never physically reissued");
        }

        static void SteeredParkingManeuvers()
        {
            using var fixture = new Fixture();
            fixture.Root.transform.position = new Vector3(10000f, 0f, 10000f);
            var plan = ParkingBlockPlan.Generate(30f, 15f);
            var site = (ParkingBlockSite)Activator.CreateInstance(typeof(ParkingBlockSite),
                Private, null, new object[] { plan, fixture.Root.transform, new Rect(),
                    ParkingEntrySide.South, ParkingBlockStyle.Attended, null, null }, null);
            var net = new LaneNet();
            net.AddRoad(new Vector3(9990f, 0f, 9990f), new Vector3(10050f, 0f, 9990f),
                8f, new[] { 2.5f }, 10f, null, null, false);
            var lot = new ParkingLot(site, net, 0, 1987, fixture.Root.transform);
            var cars = new List<ParkingCar>();
            var queryType = typeof(ParkingLot).Assembly.GetType("RoadDemo.ParkingManeuver");
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var car = new ParkingCar { Net = net, Tf = fixture.Man().Tf, HalfLen = 3.081f, HalfWide = 1.148f };
                    int index = Mathf.Min(plan.Stalls.Count - 1, Mathf.FloorToInt((i + 0.5f) * plan.Stalls.Count / 5f));
                    Call(car, "Bind", lot, plan.Stalls[index], i, 1f, Vector2.one * 20f, Vector2.one * 20f, new System.Random(i));
                    cars.Add(car);
                    ((IList)Read(lot, "_cars")).Add(car);
                }
                for (int i = 0; i < cars.Count; i++)
                    for (int exit = 0; exit < 2; exit++)
                    {
                        var car = cars[i];
                        var gate = site.Root.TransformPoint(plan.GateInside);
                        var goal = exit != 0 ? gate : car.Position;
                        var goalForward = exit != 0 ? Vector3.back : car.RoadForward;
                        var query = Activator.CreateInstance(queryType, new object[] { site, cars, car,
                            exit != 0 ? car.Position : gate, exit != 0 ? car.RoadForward : Vector3.forward, goal, goalForward });
                        for (int step = 0; step < 400 && !(bool)queryType.GetProperty("Finished").GetValue(query); step++)
                            queryType.GetMethod("Step").Invoke(query, new object[] { 256 });
                        Require((bool)queryType.GetProperty("Found").GetValue(query), $"no steering route for bay {i}, exit {exit}");
                        var path = (IList)queryType.GetField("Path").GetValue(query);
                        Require(path.Count > 2, "parking maneuver skipped physical travel");
                        for (int poseIndex = 1; poseIndex < path.Count; poseIndex++)
                        {
                            var before = path[poseIndex - 1];
                            var after = path[poseIndex];
                            var from = (Vector3)before.GetType().GetField("Position").GetValue(before);
                            var to = (Vector3)after.GetType().GetField("Position").GetValue(after);
                            var facing = (Quaternion)before.GetType().GetField("Rotation").GetValue(before);
                            var turned = (Quaternion)after.GetType().GetField("Rotation").GetValue(after);
                            for (int sample = 0; sample <= 20; sample++)
                            {
                                var position = Vector3.Lerp(from, to, sample / 20f);
                                var rotation = Quaternion.Slerp(facing, turned, sample / 20f);
                                foreach (var other in cars)
                                    if (other != car)
                                        Require(!RoadSpace.Overlap(position, rotation * Vector3.forward, car.HalfLen, car.HalfWide,
                                            other.Position, other.RoadForward, other.HalfLen, other.HalfWide, RoadSpace.Air, out _),
                                            "parking playback crossed an occupied neighbouring bay between steering poses");
                            }
                        }
                        var last = path[path.Count - 1];
                        Require(Vector3.Distance((Vector3)last.GetType().GetField("Position").GetValue(last), goal) < 0.01f,
                            "parking route stopped short of its bay or gate");
                    }
            }
            finally { foreach (var car in cars) RetireCar(car); lot.Dispose(); }
        }

        static void ParkingCatalogueFits()
        {
            var choose = typeof(ParkingLot).GetMethod("FitsParkingStall", StaticPrivate);
            bool sawOversized = false;
            foreach (var prefab in (IEnumerable<GameObject>)typeof(CoreRoads).GetProperty("CarPrefabs",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
            {
                var bounds = new Bounds();
                bool first = true;
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>())
                {
                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                if (bounds.size.z <= ParkingBlockPlan.StallDepth && bounds.size.x <= ParkingBlockPlan.StallWidth) continue;
                sawOversized = true;
                Require(!(bool)choose.Invoke(null, new object[] { prefab }), "oversized vehicle admitted to a small bay: " + prefab.name);
            }
            Require(sawOversized, "catalogue fixture did not contain the oversized pickups");
            for (int seed = 1; seed <= 5; seed++)
            {
                var dice = new System.Random(seed);
                for (int i = 0; i < 15; i++)
                    Require(CoreRoads.PickCar(dice, p => (bool)choose.Invoke(null, new object[] { p })) != null,
                        "fit filter removed all usable parking cars");
            }
        }

        static void ParkingYawWithTranslation()
        {
            using var fixture = new Fixture();
            var curve = PatrolDocking.Sweep(new Vector3(10273.1f, 0f, 10218f), Vector3.back,
                new Vector3(10265f, 0f, 10218f), Vector3.back);
            float before = 0.008268297f;
            float after = PatrolDocking.Advance(curve, before, 0.1f);
            var car = new ParkingCar { Tf = fixture.Man().Tf, HalfLen = 2.78709674f, HalfWide = 1.1827606f };
            var other = new RoadCar { Tf = fixture.Man().Tf, HalfLen = 3.173651f, HalfWide = 1.3f };
            car.Tf.SetPositionAndRotation(PatrolDocking.Point(curve, before), PatrolDocking.Heading(curve, before, Quaternion.identity));
            car.Slid(car.Tf.position, car.Tf.forward);
            other.Slid(new Vector3(10275.8f, 0f, 10223.8f), Vector3.back);
            StreetTraffic.Users.Add(car);
            StreetTraffic.Users.Add(other);
            try
            {
                var position = PatrolDocking.Point(curve, after);
                var rotation = PatrolDocking.Heading(curve, after, Quaternion.identity);
                Require((bool)Call(car, "MotionClear", position, rotation),
                    "a clear combined translation and turn was trapped by rotating in place first");
                other.Slid(position, Vector3.back);
                // Force the spatial index to account for the moved obstacle.
                typeof(RoadSpace).GetMethod("Invalidate", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Require(!(bool)Call(car, "MotionClear", position, rotation), "combined motion ignored a real occupied destination");
            }
            finally { RetireCar(car); RetireCar(other); }
        }

        static void DestinationWhileCrossing()
        {
            using var fixture = new Fixture();
            var net = new LaneNet();
            var a = net.AddNode(10000f, 10000f, 5f, 5f, 5.7f);
            var b = net.AddNode(10100f, 10000f, 5f, 5f, 5.7f);
            var c = net.AddNode(10200f, 10000f, 5f, 5f, 5.7f);
            var d = net.AddNode(10200f, 10100f, 5f, 5f, 5.7f);
            var ab = net.AddRoad(new Vector3(a.XMax, 0f, a.Z), new Vector3(b.XMin, 0f, b.Z),
                5f, new[] { 2.5f }, 10f, a, b, false);
            var bc = net.AddRoad(new Vector3(b.XMax, 0f, b.Z), new Vector3(c.XMin, 0f, c.Z),
                5f, new[] { 2.5f }, 10f, b, c, false);
            var cd = net.AddRoad(new Vector3(c.X, 0f, c.ZMax), new Vector3(d.X, 0f, d.ZMin),
                5f, new[] { 2.5f }, 10f, c, d, true);
            net.Finish();
            var car = new RoadCar { Net = net, Tf = fixture.Man().Tf, Profile = DriverProfile.Traffic };
            try
            {
                car.Spawn(ab.LaneFor(1, 2.5f), ab.Length - 15f);
                for (int i = 0; i < 600 && car.Via == null; i++) car.Tick(1f / 30f);
                Require(car.Via != null && car.Road == null, "fixture never entered its first crossing");
                var goal = cd.Pose(50f, 2.5f);
                Require(car.GoTo(goal, false, wantHeading: 1), "crossing-time order was rejected");
                bool routed = false;
                for (int i = 0; i < 2400 && car.HasGoal; i++)
                {
                    car.Tick(1f / 30f);
                    routed |= car.Route != null;
                }
                Require(routed, "crossing-time order kept wandering without a route table");
                Require(car.AtGoal && Vector3.Distance(car.Position, goal) < 2f,
                    "crossing-time order did not physically reach its destination");
            }
            finally { RetireCar(car); }
        }

        static void ParkingExitNeighbour()
        {
            using var fixture = new Fixture();
            fixture.Root.transform.position = new Vector3(10030f, 0f, 10015f);
            var net = new LaneNet();
            net.AddRoad(new Vector3(10000f, 0f, 10000f), new Vector3(10100f, 0f, 10000f),
                8f, new[] { 2.5f }, 10f, null, null, false);
            var plan = ParkingBlockPlan.Generate(40f, 30f);
            var site = (ParkingBlockSite)Activator.CreateInstance(typeof(ParkingBlockSite),
                Private, null, new object[] { plan, fixture.Root.transform, new Rect(),
                    ParkingEntrySide.South, ParkingBlockStyle.Attended, null, null }, null);
            var lot = new ParkingLot(site, net, 0, 1987, fixture.Root.transform);
            var cars = new List<ParkingCar>();
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    var car = new ParkingCar { Net = net, Tf = fixture.Man().Tf, HalfLen = 3.081f, HalfWide = 1.148f };
                    var stall = new ParkingBlockPlan.Stall(new Vector3(9.2f + i * 2.7f, 0f, 8.8f), Vector3.back,
                        new Vector3(9.2f + i * 2.7f, 0f, 3f), new Vector3(20f, 0f, 3f));
                    Call(car, "Bind", lot, stall, i, 1f, Vector2.one * 20f, Vector2.one * 20f, new System.Random(i));
                    cars.Add(car);
                    ((IList)Read(lot, "_cars")).Add(car);
                    StreetTraffic.Users.Add(car);
                }
                Call(lot, "TryUseDrive", cars[0]);
                Call(cars[0], "BeginExit");
                for (int i = 0; i < 1800 && cars[0].State == ParkingCar.Mode.Exiting; i++)
                {
                    cars[0].TickParking(1f / 30f);
                    Require(!RoadSpace.Overlap(cars[0].Position, cars[0].RoadForward, cars[0].HalfLen, cars[0].HalfWide,
                        cars[1].Position, cars[1].RoadForward, cars[1].HalfLen, cars[1].HalfWide, 0f, out _),
                        "large car swept its tail through its neighbour");
                }
                Require(cars[0].State == ParkingCar.Mode.Driving, "large car remained trapped in its stall");
            }
            finally { foreach (var car in cars) RetireCar(car); lot.Dispose(); }
        }

        static void ParkingMotionBody()
        {
            using var fixture = new Fixture();
            var car = new ParkingCar { Tf = fixture.Man().Tf };
            var other = new RoadCar { Tf = fixture.Man().Tf };
            var start = car.Tf.position;
            car.Slid(start, Vector3.forward);
            other.Slid(start + Vector3.forward * 7f, Vector3.forward);
            StreetTraffic.Users.Add(car);
            StreetTraffic.Users.Add(other);
            try
            {
                Call(car, "AddSweep", start, Vector3.forward,
                    start + Vector3.forward * 15f, Vector3.forward);
                Call(car, "StartMotions");
                Write(car, "<State>k__BackingField", ParkingCar.Mode.Entering);
                for (int i = 0; i < 150; i++) Call(car, "TickMotion", 1f / 30f);
                Require(car.Position.z > start.z + 0.1f && car.Position.z < other.Position.z - car.HalfLen,
                    "parking curve failed to stop before the occupied body");
                Require(!RoadSpace.Overlap(car.Position, car.RoadForward, car.HalfLen, car.HalfWide,
                    other.Position, other.RoadForward, other.HalfLen, other.HalfWide, 0f, out _),
                    "parking motion drove through another car");
                car.Slid(car.Position, Vector3.right);
                Require(Vector3.Dot(car.RoadForward, Vector3.right) > 0.999f,
                    "manual car turn left its collision body facing the old direction");
            }
            finally { RetireCar(car); RetireCar(other); }
        }

        static void FailedRouteChanges()
        {
            using var fixture = new Fixture();
            var start = fixture.Man().Tf.position;
            var target = start + Vector3.right * 8f;
            var plan = new SidewalkPlan();
            var oldMin = WalkObstacles.Min;
            var oldMax = WalkObstacles.Max;
            var oldCity = WalkObstacles.City.ToArray();
            foreach (var shift in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
                plan.Take(new SidewalkPlan.Box {
                    C = new Vector2(target.x, target.z) + shift * 2.5f,
                    H = shift.x != 0 ? new Vector2(0.5f, 3f) : new Vector2(3f, 0.5f),
                    Ax = Vector2.right, Az = Vector2.up, Solid = true });
            WalkObstacles.RegisterPlan(plan);
            try
            {
                WalkObstacles.City.Clear();
                WalkObstacles.City.Add(new Rect(start.x - 5f, start.z - 8f, 22f, 16f));
                WalkObstacles.Version++;
                var path = new List<Vector3>();
                Require(!WalkRoute.Plan(start, target, path), "route entered a sealed enclosure");
                var visit = typeof(WalkRoute).GetField("_visit", StaticPrivate);
                var searched = visit.GetValue(null);
                path.Add(start);
                Require(!WalkRoute.Plan(start, target, path) && path.Count == 0 &&
                    Equals(searched, visit.GetValue(null)), "unchanged impossible route searched the city again");
                Require(!WalkRoute.Plan(start + Vector3.left * 0.2f, target + Vector3.forward * 0.2f, path) &&
                    Equals(searched, visit.GetValue(null)), "drifting endpoints searched the whole city toward a sealed destination");
                WalkObstacles.UnregisterPlan(plan);
                Require(WalkRoute.Plan(start, target, path), "removed enclosure left a stale failed route");
            }
            finally
            {
                WalkObstacles.UnregisterPlan(plan);
                WalkObstacles.City.Clear();
                WalkObstacles.City.AddRange(oldCity);
                WalkObstacles.Min = oldMin;
                WalkObstacles.Max = oldMax;
                WalkObstacles.Version++;
            }
        }

        static void DestroyedMemberDuringMarch()
        {
            using var fixture = new Fixture();
            var crews = fixture.Root.AddComponent<DemoCrews>();
            var boss = fixture.Man();
            var retired = fixture.Man();
            var unit = fixture.Unit(boss);
            unit.Hoods.Add(retired);
            var world = LivingCity.Outfit.Underworld.Deal(1987, 2);
            retired.CharacterId = world.Player.Roster.BossId;
            retired.Faction = 0;
            ((IDictionary)Read(crews, "_byCharacter")).Add(retired.CharacterId, retired);
            Require(!(bool)Call(crews, "HasMissingActiveView", world), "intact member requested a body rebuild");
            Object.DestroyImmediate(retired.Tf.gameObject);
            Require((bool)Call(crews, "HasMissingActiveView", world), "missing living view waited for another roster mutation");
            Require((bool)Call(crews, "DispatchAcross", unit, boss,
                boss.Tf.position + Vector3.right * 2f, false, false),
                "a retired member prevented the standing crew from receiving a route");
        }

        static void RecognitionRetainsLoadedDocket()
        {
            var previousWorld = LivingCity.Outfit.Underworld.Current;
            var previousCrews = DemoCrews.Active;
            var previousForce = PoliceForce.Instance;
            var previousRandom = UnityEngine.Random.state;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var world = LivingCity.Outfit.Underworld.Deal(1987 + repetition, 2);
                    LivingCity.Outfit.Underworld.Restore(world);
                    var roster = world.Of(1).Roster;
                    var member = roster.Members[0];
                    LivingCity.Police.WantedLevels.Mark(member,
                        LivingCity.Police.WantedLevels.FreedFromTransfer, 1);
                    var crews = fixture.Root.AddComponent<DemoCrews>();
                    typeof(DemoCrews).GetProperty(nameof(DemoCrews.Active)).SetValue(null, crews);
                    var force = fixture.Root.AddComponent<PoliceForce>();
                    typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance)).SetValue(null, force);
                    var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
                    dispatch.Force = force;
                    Write(dispatch, "_crews", crews);
                    var man = fixture.Man(); man.CharacterId = member.Id; man.Faction = 1;
                    var unit = fixture.Unit(man); unit.Faction = 1;
                    var file = force.Pipeline.OpenCase(Deed.Extortion, 1, 1, 2);
                    file.Defendants.Add(member.Id);
                    file.Witnesses.Add(new LivingCity.Police.Witness {
                        Kind = LivingCity.Police.WitnessKind.Complainant });
                    // The saved docket survives; the rebuilt street unit has no reference.
                    Require(unit.ArrestCase == null, "fixture already remembered the case");
                    var officer = fixture.Man(); officer.Tf.position += Vector3.right * 4f;
                    var beat = new PoliceBeat(crews, fixture.Unit(officer), 1,
                        null, null, null, Vector2.zero, 0f);
                    ((IList)Read(dispatch, "_units")).Add(beat);
                    Call(dispatch, "ChaseOnSight", unit, beat, 3);
                    Require(ReferenceEquals(Read(dispatch, "_arrestCase"), file) &&
                        (Deed)Read(dispatch, "_arrestDeed") == Deed.Extortion &&
                        !(bool)Read(dispatch, "_arrestCaseIsOurs") &&
                        Read(dispatch, "_collar").ToString() == "WalkingUp",
                        "recognition lost the loaded charge or replaced it with resisting");
                    Call(dispatch, "Clear", true);
                }
            }
            finally
            {
                LivingCity.Outfit.Underworld.Restore(previousWorld);
                typeof(DemoCrews).GetProperty(nameof(DemoCrews.Active)).SetValue(null, previousCrews);
                typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance)).SetValue(null, previousForce);
                UnityEngine.Random.state = previousRandom;
            }
        }

        static void HiddenArrestTarget()
        {
            using var fixture = new Fixture();
            var lieutenant = fixture.Man();
            var hood = fixture.Man();
            hood.Tf.position += Vector3.right * 2f;
            var unit = fixture.Unit(lieutenant);
            var lawman = fixture.Man();
            lawman.Tf.position += Vector3.right * 4f;
            var choose = typeof(PoliceDispatch).GetMethod("StreetCollar", StaticPrivate);
            Require(ReferenceEquals(lieutenant, choose.Invoke(null,
                new object[] { unit, lawman.Tf.position, true, null })), "visible lieutenant was not selected");
            DoorBeat.RestoreInside(lieutenant, lieutenant.Tf.position);
            Require(choose.Invoke(null, new object[] { unit, lawman.Tf.position, true, null }) == null,
                "a hidden lieutenant was recognised through his unit marker");
            unit.Hoods.Add(hood);
            Require(ReferenceEquals(hood, choose.Invoke(null,
                new object[] { unit, lawman.Tf.position, true, null })), "visible hood was replaced by his hidden lieutenant");
            var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
            Write(dispatch, "_arrestCrew", unit);
            Write(dispatch, "_arrestCollar", lieutenant);
            Write(dispatch, "_arrestLawman", lawman);
            Write(dispatch, "_collar", Enum.Parse(typeof(PoliceDispatch).GetNestedType("Collar", BindingFlags.NonPublic), "WalkingUp"));
            Write(dispatch, "_collarBy", Time.time + 45f);
            Require((bool)Call(dispatch, "ArrestOff") && Read(dispatch, "_collar").ToString() == "None",
                "a suspect entering a building left the global arrest occupied until timeout");
        }

        static void ShortCustodyRoute()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var start = man.Tf.position;
            var destination = start + Vector3.right * 12f;
            var plan = new SidewalkPlan();
            var oldMin = WalkObstacles.Min;
            var oldMax = WalkObstacles.Max;
            var oldCity = WalkObstacles.City.ToArray();
            plan.Take(new SidewalkPlan.Box { C = new Vector2(start.x + 6f, start.z),
                H = new Vector2(3.5f, 1.3f), Ax = Vector2.right, Az = Vector2.up, Solid = true });
            WalkObstacles.RegisterPlan(plan);
            try
            {
                WalkObstacles.City.Clear();
                WalkObstacles.City.Add(new Rect(start.x - 20f, start.z - 20f, 50f, 40f));
                WalkObstacles.Min = new Vector2(start.x - 20f, start.z - 20f);
                WalkObstacles.Max = new Vector2(start.x + 30f, start.z + 20f);
                WalkObstacles.Version++;
                var accepted = (bool)typeof(PrisonerCarriage).GetMethod("OrderCustodyLeg", StaticPrivate)
                    .Invoke(null, new object[] { man, destination, true });
                var legs = (List<Vector3>)Read(man, "_legs");
                Require(accepted && legs.Count > 1, "short custody leg was left to direct steering across the parked props");
                var previous = start;
                foreach (var corner in legs)
                {
                    Require(!WalkObstacles.BlocksStanding(previous, corner, WalkRoute.ClearanceRadius),
                        "custody route crosses a parked prop");
                    previous = corner;
                }
                Require(Vector3.Distance(previous, destination) < 0.05f, "escort route did not reach the prisoner");
            }
            finally
            {
                WalkObstacles.UnregisterPlan(plan);
                WalkObstacles.City.Clear();
                WalkObstacles.City.AddRange(oldCity);
                WalkObstacles.Min = oldMin;
                WalkObstacles.Max = oldMax;
                WalkObstacles.Version++;
            }
        }

        static void RivalArrivesAtCourt()
        {
            using var fixture = new Fixture();
            var current = typeof(LivingCity.Outfit.Underworld).GetProperty("Current");
            var previous = LivingCity.Outfit.Underworld.Current;
            var world = LivingCity.Outfit.Underworld.Deal(1987, 2);
            current.SetValue(null, world);
            try
            {
                var roster = world.Of(1).Roster;
                var id = roster.Members[1].Id;
                var force = fixture.Root.AddComponent<PoliceForce>();
                var file = force.Pipeline.OpenCase(LivingCity.Personnel.Deed.Affray, 1, 0, 1);
                var prisoner = force.Pipeline.Book(roster, id, LivingCity.Personnel.Deed.Affray, 0, file);
                Require(prisoner != null, "rival booking fixture failed");
                prisoner.Stage = LivingCity.Police.PrisonStage.InTransit;
                prisoner.Leg = LivingCity.Police.PrisonLeg.Court;
                var body = fixture.Man(); body.CharacterId = id;
                var type = typeof(PoliceForce).GetNestedType("Convoy", BindingFlags.NonPublic);
                var convoy = Activator.CreateInstance(type, true);
                type.GetField("Carriage").SetValue(convoy,
                    new PrisonerCarriage(id, body, null, null, null, null));
                ((IList)type.GetField("Riders").GetValue(convoy)).Add(prisoner);
                Call(force, "CompleteCourtThreshold", convoy);
                Require(file.VerdictFor(id) != null &&
                        prisoner.Stage == LivingCity.Police.PrisonStage.Cleared &&
                        roster.Find(id).Status == LivingCity.Personnel.CharacterStatus.Active,
                    "a rival reached court but was tried against the player's roster");
            }
            finally { current.SetValue(null, previous); }
        }

        static void DestroyedCombatObserver()
        {
            using var fixture = new Fixture();
            var observer = fixture.Man();
            var target = fixture.Man();
            target.Tf.position += Vector3.right * 4f;
            var ours = fixture.Unit(observer);
            var theirs = fixture.Unit(target);
            theirs.Faction = 1;
            var crews = fixture.Root.AddComponent<DemoCrews>();
            crews.Units.Add(ours); crews.Units.Add(theirs);
            Require(Call(crews, "EnemyWithin", ours, 20f, true, false) == theirs,
                "setup did not establish a visible enemy");
            Object.DestroyImmediate(observer.Tf.gameObject);
            Require(Call(crews, "EnemyWithin", ours, 20f, true, false) == null,
                "a destroyed observer saw an enemy");
        }

        static void DestroyedCarCollisionGhost()
        {
            var net = new LaneNet();
            var road = net.AddRoad(new Vector3(10000f, 0, 10000f),
                new Vector3(10100f, 0, 10000f), 8f, new[] { 2.5f }, 10f,
                null, null, true);
            var body = new GameObject("destroyed car view");
            var car = new RoadCar { Net = net, Tf = body.transform };
            try
            {
                car.PlaceAt(road.Pose(50f, 2.5f), road.Axis);
                StreetTraffic.Users.Add(car);
                Object.DestroyImmediate(body);
                RoadCar.PruneRegistered();
                Require(!StreetTraffic.Users.Contains(car) && !car.OnRoad,
                    "destroying a car view retained its collision body or lane claims");
            }
            finally { RetireCar(car); if (body) Object.DestroyImmediate(body); }
        }

        static void StalledFootResponse()
        {
            using var fixture = new Fixture();
            var unit = fixture.Unit(fixture.Man());
            var crews = fixture.Root.AddComponent<DemoCrews>();
            crews.Units.Add(unit);
            var beat = new PoliceBeat(crews, unit, 2, null, null, null, Vector2.zero, 0f);
            Write(beat, "<State>k__BackingField", PoliceBeat.Mode.Responding);
            Write(beat, "<StalledOnTheWay>k__BackingField", true);
            var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
            Write(dispatch, "_crews", crews);
            var type = typeof(PoliceDispatch).GetNestedType("Squad", BindingFlags.NonPublic);
            var squad = Activator.CreateInstance(type, true);
            type.GetField("Ride").SetValue(squad, beat);
            type.GetField("Men").SetValue(squad, unit);
            var state = type.GetField("State");
            state.SetValue(squad, Enum.Parse(state.FieldType, "Sent"));
            ((IList)Read(dispatch, "_squads")).Add(squad);
            Call(dispatch, "TickSquad", squad, 0.1f);
            Require(beat.Available && !(bool)Call(dispatch, "ResponseOwns", beat),
                "a sent response retained a stalled beat indefinitely");
        }

        static void SimultaneousComplaintArrests()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var suspect = fixture.Unit(man);
            var crews = fixture.Root.AddComponent<DemoCrews>();
            crews.Units.Add(suspect);
            var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
            Write(dispatch, "_crews", crews);
            var collar = typeof(PoliceDispatch).GetField("_collar", Private);
            collar.SetValue(dispatch, Enum.Parse(collar.FieldType, "Asking"));
            var callType = typeof(PoliceDispatch).GetNestedType("CallOut", BindingFlags.NonPublic);
            var call = Activator.CreateInstance(callType, true);
            callType.GetField("Call").SetValue(call, new StreetAlarm.Complaint
                { Pos = man.Tf.position, Faction = suspect.Faction });
            callType.GetField("AtTheDoorstep").SetValue(call, true);
            var stage = callType.GetField("Stage");
            stage.SetValue(call, Enum.Parse(stage.FieldType, "AtTheDoor"));
            Call(dispatch, "AtTheDoor", call);
            Require(stage.GetValue(call).ToString() == "AtTheDoor",
                "another active arrest made the officer abandon a present suspect");
            man.Tf.gameObject.SetActive(false);
            Require(Call(dispatch, "AccusedNear", man.Tf.position, suspect.Faction) == null,
                "an indoor hidden collector was selected as the doorstep suspect");
        }

        static void StationaryPassingReservation()
        {
            var net = new LaneNet();
            var road = net.AddRoad(new Vector3(10000f, 0, 10000f),
                new Vector3(10100f, 0, 10000f), 8f, new[] { 2.5f }, 10f,
                null, null, true);
            var car = new RoadCar { Net = net, Profile = DriverProfile.Police,
                HalfLen = 3.7235f, HalfWide = 1.2842f };
            try
            {
                car.PlaceAt(road.Pose(70.92f, 2.5f), road.Axis);
                var at = car.Position;
                Call(car, "Slide", 0.6f, 7.87f);
                Call(car, "Claim", 67.2f, 99.65f, -0.984f, 3.784f);
                Call(car, "UpdateOccupant");
                Require(road.Occupants.Exists(o => o.Who == car && o.S1 > 99f),
                    "setup did not reserve the blocked passing corridor");
                for (int i = 0; i < 7; i++) Call(car, "ReleaseStalledLateral", 1f);
                Require(!car.Sliding && car.Position == at,
                    "cancelled stationary sweep moved the car or retained the slide");
                Require(road.Occupants.Exists(o => o.Who == car && o.S1 < 75f),
                    "stationary car kept claiming the road ahead of its actual body");
            }
            finally { RetireCar(car); }
        }

        static void SubwayBlocksSideShop()
        {
            for (int seed = 1987; seed < 1992; seed++)
            {
                var plan = ResidentialLot.Roll(17, 17, seed);
                Require(plan.Subway == null && plan.M.Subways == 0,
                    "residential generation still placed a subway for seed " + seed);
            }
        }

        static void CustodyOwnsCar()
        {
            var car = new PolicePatrolCar();
            try
            {
                Write(car, "<State>k__BackingField", PolicePatrolCar.Mode.Returning);
                Require(car.Available, "empty returning patrol should answer a call");
                car.HoldAtKerb = true;
                Require(!car.Available, "a car carrying prisoners answered another call");
                car.HoldAtKerb = false;
                typeof(PolicePatrolCar).GetProperty("CustodyReserved", Private).SetValue(car, true);
                Require(!car.Available, "unloading between custody waves released the car");
                typeof(PolicePatrolCar).GetProperty("CustodyReserved", Private).SetValue(car, false);
                Require(car.Available, "completed custody did not return the car to service");
            }
            finally { RetireCar(car); }
        }

        static void RetiredWalkerTick()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            Object.DestroyImmediate(man.Tf.gameObject);
            man.TickCrew(0.05f);
        }

        static void ResponseParkingDestination()
        {
            var car = new PolicePatrolCar();
            try
            {
                Write(car, "<State>k__BackingField", PolicePatrolCar.Mode.Responding);
                Write(car, "_scenePos", new Vector3(10000f, 0f, 10000f));
                Write(car, "_responseParkingReachSq", 30f * 30f);
                Require((bool)Call(car, "ParkingSpotAvailable", new Vector3(10020f, 0f, 10000f)),
                    "a nearby replacement kerb was refused");
                Require(!(bool)Call(car, "ParkingSpotAvailable", new Vector3(10300f, 0f, 10000f)),
                    "a response could replace its pickup with a kerb across town");
                Write(car, "<State>k__BackingField", PolicePatrolCar.Mode.Parking);
                Require((bool)Call(car, "ParkingSpotAvailable", new Vector3(10300f, 0f, 10000f)),
                    "ordinary patrol rest parking inherited the response restriction");
            }
            finally { RetireCar(car); }
        }

        static void ShoulderBlocksPullOut()
        {
            var net = new LaneNet();
            var road = net.AddRoad(new Vector3(10000f, 0f, 10000f),
                new Vector3(10000f, 0f, 10040f), 8f, new[] { 2.5f }, 10f,
                null, null, true);
            var car = new RoadCar { Net = net, Profile = DriverProfile.Police,
                HalfLen = 3.723523f, HalfWide = 1.28418f };
            try
            {
                car.PlaceAt(road.Pose(20.26468f, 0.6f), road.Axis);
                road.Occupants.Add(new RoadOccupant { Road = road, Parked = true,
                    S0 = 24.64108f, S1 = 31.31982f, D0 = 4.455125f, D1 = 8.02671f });
                road.Occupants.Add(new RoadOccupant { Road = road, Parked = true,
                    S0 = 32.85902f, S1 = 37.10638f, D0 = 2.662f, D1 = 7.72827f });
                road.Occupants.Add(new RoadOccupant { Road = road,
                    S0 = 8.18f, S1 = 14.34115f, D0 = 1.35202f, D1 = 3.64798f });
                car.PullOut();
                Write(car, "_pullOutAsked", Time.time - 30f);
                var start = car.Position;
                Call(car, "TickPullOut");
                Require(!(bool)Read(car, "_pullOutWanted"),
                    "a permanently obstructed swing kept waiting despite a clear forward band");
                Require(car.Position == start, "pull-out recovery teleported the car");
                Require((float)Read(car, "_yieldUntil") > Time.time,
                    "lane keeping immediately retried the same blocked swing");
            }
            finally { RetireCar(car); }
        }

        static void Check(List<string> failures, string name, Action test)
        {
            try { test(); }
            catch (Exception error) { failures.Add(name + ": " + error.GetBaseException().Message); }
        }

        static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        static void RetireCar(RoadCar car)
        {
            if (car.Tf != null) Object.DestroyImmediate(car.Tf.gameObject);
            car.Vanish();
        }

        static object Read(object target, string name) =>
            target.GetType().GetField(name, Private).GetValue(target);
        static void Write(object target, string name, object value) =>
            target.GetType().GetField(name, Private).SetValue(target, value);
        static object Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, Private).Invoke(target, args);

        // A stopped demo can retain navigation ledgers when domain reload is off.
        // Its city near the origin and fixtures at 10km must not become one lattice.
        sealed class NavigationScope : IDisposable
        {
            readonly object _solids = typeof(WalkObstacles).GetField("_solids", StaticPrivate).GetValue(null);
            readonly object _composed = typeof(WalkObstacles).GetField("_composedProps", StaticPrivate).GetValue(null);
            readonly List<SidewalkPlan> _plans = new List<SidewalkPlan>(WalkObstacles.Props);
            readonly Rect[] _city = WalkObstacles.City.ToArray();
            readonly Vector2 _min = WalkObstacles.Min;
            readonly Vector2 _max = WalkObstacles.Max;

            public NavigationScope()
            {
                foreach (var plan in _plans) WalkObstacles.UnregisterPlan(plan);
                typeof(WalkObstacles).GetField("_solids", StaticPrivate).SetValue(null, new SidewalkPlan());
                typeof(WalkObstacles).GetField("_composedProps", StaticPrivate).SetValue(null, new SidewalkPlan());
                WalkObstacles.City.Clear();
                WalkObstacles.Min = new Vector2(float.MaxValue, float.MaxValue);
                WalkObstacles.Max = new Vector2(float.MinValue, float.MinValue);
                ResetRoute();
            }

            public void Dispose()
            {
                foreach (var plan in new List<SidewalkPlan>(WalkObstacles.Props)) WalkObstacles.UnregisterPlan(plan);
                typeof(WalkObstacles).GetField("_solids", StaticPrivate).SetValue(null, _solids);
                typeof(WalkObstacles).GetField("_composedProps", StaticPrivate).SetValue(null, _composed);
                foreach (var plan in _plans) WalkObstacles.RegisterPlan(plan);
                WalkObstacles.City.Clear(); WalkObstacles.City.AddRange(_city);
                WalkObstacles.Min = _min; WalkObstacles.Max = _max;
                ResetRoute();
            }

            static void ResetRoute()
            {
                WalkObstacles.Version++;
                typeof(WalkRoute).GetMethod("Forget", StaticPrivate).Invoke(null, null);
            }
        }

        sealed class Fixture : IDisposable
        {
            public readonly GameObject Root = new GameObject("MiniCore regression")
                { hideFlags = HideFlags.HideAndDontSave };
            public readonly List<CrewWalker> Men = new List<CrewWalker>();
            public readonly List<DemoCrews.Unit> Units = new List<DemoCrews.Unit>();
            readonly DoorBeat oldDoorBeat = Object.FindAnyObjectByType<DoorBeat>();
            readonly Rect testGround = new Rect(9950f, 9950f, 600f, 600f);
            readonly float oldShotAt = StreetAlarm.LastShotAt;
            readonly Vector3 oldShotPos = StreetAlarm.LastShotPos;

            public Fixture()
            {
                // Gunfire cases run in the same stopped editor. Their last shot
                // must not refuse the next fixture's unrelated doorway setup.
                typeof(StreetAlarm).GetProperty(nameof(StreetAlarm.LastShotAt)).SetValue(null, -1000f);
                // The stopped live scene can leave its city fence registered. These
                // isolated bodies still need their own walkable test ground.
                WalkObstacles.City.Add(testGround);
                WalkObstacles.Version++;
            }

            public CrewWalker Man()
            {
                var body = new GameObject("Test man");
                body.transform.SetParent(Root.transform);
                body.transform.position = new Vector3(10000f, 0f, 10000f);
                var man = new CrewWalker { Tf = body.transform, CharacterId = -1 };
                Men.Add(man);
                return man;
            }

            public DemoCrews.Unit Unit(CrewWalker boss)
            {
                var unit = new DemoCrews.Unit { Boss = boss, Faction = 0 };
                Units.Add(unit);
                return unit;
            }

            public void Dispose()
            {
                WalkObstacles.City.Remove(testGround);
                WalkObstacles.Version++;
                foreach (var unit in Units) CrewQuarters.Forget(unit);
                foreach (var man in Men) DoorBeat.Evict(man);
                if (oldDoorBeat == null)
                {
                    var beat = Object.FindAnyObjectByType<DoorBeat>();
                    if (beat != null) Object.DestroyImmediate(beat.gameObject);
                }
                Object.DestroyImmediate(Root);
                typeof(StreetAlarm).GetProperty(nameof(StreetAlarm.LastShotAt)).SetValue(null, oldShotAt);
                typeof(StreetAlarm).GetProperty(nameof(StreetAlarm.LastShotPos)).SetValue(null, oldShotPos);
            }
        }

        static void OrphanDoorway()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var unit = fixture.Unit(man);
            DoorBeat.RestoreInside(man, man.Tf.position);
            Require(DoorBeat.Held(man) && !CrewQuarters.Billeted(unit), "setup did not reproduce the orphan hold");
            CrewQuarters.Retasked(unit);
            Require(man.Tf.gameObject.activeSelf && !DoorBeat.Active(man),
                "retasking left a living, commandable man invisible");
        }

        static void LateEscort()
        {
            using var fixture = new Fixture();
            var collector = fixture.Man();
            var unit = fixture.Unit(collector);
            unit.IsDetachment = true;
            DoorBeat.RestoreInside(collector, collector.Tf.position);
            var quarters = typeof(CrewQuarters);
            var key = quarters.GetMethod("Key", StaticPrivate).Invoke(null, new object[] { unit });
            var billet = Activator.CreateInstance(quarters.GetNestedType("Billet", BindingFlags.NonPublic), true);
            billet.GetType().GetField("Unit").SetValue(billet, unit);
            billet.GetType().GetField("Doorstep").SetValue(billet, collector.Tf.position);
            billet.GetType().GetField("In").SetValue(billet, true);
            var billets = (IDictionary)quarters.GetField("Billets", StaticPrivate).GetValue(null);
            billets.Add(key, billet);
            var escort = fixture.Man();
            unit.Hoods.Add(escort);
            CrewQuarters.Tick(fixture.Root.AddComponent<DemoCrews>());
            Require(!CrewQuarters.Inside(unit) && DoorBeat.Active(escort),
                "an escort joining an indoor collector was left idle outside");
        }

        static void HiddenSuspect()
        {
            using var fixture = new Fixture();
            var man = fixture.Man();
            var suspect = fixture.Unit(man);
            var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
            ((IList)Read(dispatch, "_hunted")).Add(suspect);
            var policeBody = new GameObject("Test patrol");
            policeBody.transform.SetParent(fixture.Root.transform);
            var patrol = new PolicePatrolCar { Tf = policeBody.transform };
            try
            {
                policeBody.transform.position = man.Tf.position + Vector3.right * 4f;
                patrol.Slid(policeBody.transform.position);
                ((IList)Read(dispatch, "_units")).Add(patrol);
                Require((bool)Call(dispatch, "AnyHuntedSeen"), "an exposed nearby suspect was not seen");
                man.Tf.gameObject.SetActive(false);
                Require(!(bool)Call(dispatch, "AnyHuntedSeen"), "an invisible indoor suspect kept the swarm alive");
                man.Tf.gameObject.SetActive(true);
                policeBody.SetActive(false);
                Require(!(bool)Call(dispatch, "AnyHuntedSeen"), "an inactive patrol kept observing suspects");
            }
            finally { patrol.Tf = null; patrol.Vanish(); }
        }

        static void ResolvedSwarm()
        {
            using var fixture = new Fixture();
            var suspect = fixture.Unit(fixture.Man());
            var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
            Write(dispatch, "_swarm", true);
            ((IList)Read(dispatch, "_hunted")).Add(suspect);
            suspect.Surrendered = true;
            var squadType = typeof(PoliceDispatch).GetNestedType("Squad", BindingFlags.NonPublic);
            var squad = Activator.CreateInstance(squadType, true);
            squadType.GetField("SwarmResponse").SetValue(squad, true);
            var state = squadType.GetField("State");
            state.SetValue(squad, Enum.Parse(state.FieldType, "Responding"));
            ((IList)Read(dispatch, "_squads")).Add(squad);
            Call(dispatch, "TickSwarm", .1f);
            Require(!dispatch.Swarming && state.GetValue(squad).ToString() == "Leaving",
                "the last resolved suspect left the response or its approaching squad active");
            Write(dispatch, "_swarm", true);
            suspect.Surrendered = false;
            suspect.Boss = null;
            ((IList)Read(dispatch, "_hunted")).Add(suspect);
            Call(dispatch, "TickSwarm", .1f);
            Require(!dispatch.Swarming, "a wiped crew kept the swarm active");
        }

        sealed class CrowdProbe : PedestrianAgent
        {
            public bool Clear(Vector3 from, Vector3 to) => GraphStepClear(from, to);
        }

        static void StreamedFurniture()
        {
            var plan = new SidewalkPlan();
            var from = new Vector3(10000f, 0f, 10000f);
            var to = from + Vector3.right * 4f;
            var walker = new CrowdProbe();
            Require(walker.Clear(from, to), "test corridor was already occupied");
            WalkObstacles.RegisterPlan(plan);
            try
            {
                plan.Take(new SidewalkPlan.Box { C = new Vector2(10002f, 10000f),
                    H = new Vector2(.3f, .8f), Ax = Vector2.right,
                    Az = Vector2.up, Solid = true });
                Require(!walker.Clear(from, to), "a civilian crossed furniture added after graph creation");
                Require(walker.Clear(from + Vector3.forward * 3f, to + Vector3.forward * 3f),
                    "furniture blocked a clear parallel corridor");
            }
            finally { WalkObstacles.UnregisterPlan(plan); }
        }
    }
}

