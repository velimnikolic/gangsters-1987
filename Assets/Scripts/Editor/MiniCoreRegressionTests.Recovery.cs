using System;
using System.Collections.Generic;
using RoadDemo;
using LivingCity.Police;
using UnityEngine;

namespace LivingCity.Tests
{
    public static partial class MiniCoreRegressionTests
    {
        static void TrafficRecoveryVisibility()
        {
            var previous = RoadCar.RecoveryVisibility;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    var net = ArcTestRoad(repetition * 72f, out var road);
                    var car = ArcTestCar(net, road, 1, 50f, 40f);
                    try
                    {
                        var original = car.Position;
                        RoadCar.RecoveryVisibility = _ => true;
                        Require(!(bool)Call(car, "TryRecoverTraffic", true) &&
                            !(bool)Call(car, "TryRecoverTraffic", false) && car.Position == original,
                            "a revealed car made a large relocation");
                        RoadCar.RecoveryVisibility = at => {
                            road.Project(at, out _, out float lateral);
                            return lateral < 3f;
                        };
                        Require(!(bool)Call(car, "TryRecoverTraffic", true),
                            "hidden car appeared in a revealed destination");
                        RoadCar.RecoveryVisibility = _ => false;
                        car.GoTo(road.Pose(90f, 2.5f), park: false);
                        var goal = Read(car, "_goalS");
                        Require((bool)Call(car, "TryRecoverTraffic", true) && car.TrafficRecoveries == 1 &&
                            car.LastTrafficRecoveryHidden && !car.Gone && car.HasGoal &&
                            Equals(goal, Read(car, "_goalS")) &&
                            RoadSpace.Inside(car, car.Position, car.Forward, car.HalfLen, car.HalfWide, out _) == null,
                            "hidden recovery lost its route or occupied another body");
                    }
                    finally { RetireCar(car); }
                }
            }
            finally { RoadCar.RecoveryVisibility = previous; }
        }

        static void TrafficRecoveryKeepsPrisoners()
        {
            var previousVisibility = RoadCar.RecoveryVisibility;
            var previousWorld = LivingCity.Outfit.Underworld.Current;
            var previousForce = PoliceForce.Instance;
            var previousRandom = UnityEngine.Random.state;
            try
            {
                foreach (bool hidden in new[] { true, false })
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var world = LivingCity.Outfit.Underworld.Deal(1987 + repetition, 2);
                    LivingCity.Outfit.Underworld.Restore(world);
                    var force = fixture.Root.AddComponent<PoliceForce>();
                    var roster = world.Of(1).Roster;
                    var member = roster.Members[0];
                    var file = force.Pipeline.OpenCase(LivingCity.Personnel.Deed.Affray, 1, 0, 1);
                    var record = force.Pipeline.Book(roster, member.Id, LivingCity.Personnel.Deed.Affray, 0, file);
                    Require(record != null, "custody fixture could not book its prisoner");
                    record.Stage = LivingCity.Police.PrisonStage.InTransit;
                    record.Leg = LivingCity.Police.PrisonLeg.Court;
                    var net = ArcTestRoad(repetition * 72f, out var road);
                    var car = ArcTestCar(net, road, 1, 50f, 6f);
                    car.Tf = new GameObject("Recovery custody car").transform;
                    car.Tf.SetParent(fixture.Root.transform);
                    car.Tf.SetPositionAndRotation(car.Position, Quaternion.LookRotation(car.Forward));
                    car.GoTo(road.Pose(90f, 2.5f), park: false);
                    var goal = Read(car, "_goalS");
                    var prisoner = fixture.Man(); prisoner.CharacterId = member.Id;
                    var officer = fixture.Man(); var second = fixture.Man();
                    var escort = fixture.Unit(officer); escort.Hoods.Add(second);
                    var carriage = new PrisonerCarriage(member.Id, prisoner, escort, car, null, null);
                    var seated = (List<PrisonerCarriage.SeatedBody>)Read(carriage, "_bodies");
                    var men = new[] { prisoner, officer, second };
                    var locals = new Vector3[3]; var worlds = new Vector3[3];
                    try
                    {
                        for (int i = 0; i < men.Length; i++)
                        {
                            var seat = PrisonerCarriage.SeatBody(car.Tf, men[i], i == 0 ? 2 : i - 1, i == 0, null);
                            Require(seat != null, "custody fixture could not seat a rider");
                            seated.Add(seat); locals[i] = men[i].Tf.localPosition; worlds[i] = men[i].Tf.position;
                        }
                        Write(carriage, "_prisonerSeated", true);
                        typeof(PrisonerCarriage).GetProperty("Stage").SetValue(carriage, CarriageStage.Riding);
                        var originalCar = car.Tf;
                        RoadCar.RecoveryVisibility = _ => !hidden;
                        Require((bool)Call(car, "TryRecoverTraffic", hidden), "hidden custody car did not recover");
                        Require(ReferenceEquals(carriage.Car, car) && car.Tf == originalCar &&
                            carriage.Stage == CarriageStage.Riding && carriage.PrisonerSeated && carriage.Bodies.Count == 3 &&
                            record.CaseId == file.CaseId && record.Stage == LivingCity.Police.PrisonStage.InTransit &&
                            record.Leg == LivingCity.Police.PrisonLeg.Court && Equals(goal, Read(car, "_goalS")),
                            "traffic recovery replaced its custody owner, docket, stage or destination");
                        for (int i = 0; i < men.Length; i++)
                            Require(ReferenceEquals(carriage.Bodies[i].Man, men[i]) && men[i].Tf.parent == car.Tf &&
                                (men[i].Tf.localPosition - locals[i]).sqrMagnitude < .0001f &&
                                (men[i].Tf.position - worlds[i]).sqrMagnitude > 1f,
                                "a prisoner or escort was left behind by the recovered car");
                        for (int frame = 0; frame < 9000 && car.HasGoal; frame++) car.Tick(1f / 30f);
                        Require(car.AtGoal && (car.Position - road.Pose(90f, 2.5f)).sqrMagnitude < 9f &&
                            prisoner.Tf.parent == car.Tf && record.CaseId == file.CaseId &&
                            record.Stage == LivingCity.Police.PrisonStage.InTransit,
                            "recovered custody car failed to carry its prisoner physically to the original destination");
                    }
                    finally { PrisonerCarriage.RestoreBodies(seated, car.Position); RetireCar(car); }
                }
            }
            finally
            {
                RoadCar.RecoveryVisibility = previousVisibility;
                LivingCity.Outfit.Underworld.Restore(previousWorld);
                typeof(PoliceForce).GetProperty(nameof(PoliceForce.Instance)).SetValue(null, previousForce);
                UnityEngine.Random.state = previousRandom;
            }
        }
    }
}

