using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Tests
{
    public static partial class MiniCoreRegressionTests
    {
        static void MovingTransferKeepsItsDeadline()
        {
            var convoyType = typeof(PoliceForce).GetNestedType("Convoy", BindingFlags.NonPublic);
            var begin = typeof(PoliceForce).GetMethod("BeginDrivingDeadline", StaticPrivate);
            var within = typeof(PoliceForce).GetMethod("DrivingWithinDeadline", StaticPrivate);
            var position = typeof(RoadCar).GetField("_pos", Private);
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var car = new PolicePatrolCar { Tf = new GameObject("Timed custody drive").transform };
                car.Tf.SetParent(fixture.Root.transform);
                var prisoner = fixture.Man();
                var escort = fixture.Unit(fixture.Man());
                escort.Hoods.Add(fixture.Man());
                var carriage = new PrisonerCarriage(77, prisoner, escort, car, null, null);
                var seats = (List<PrisonerCarriage.SeatedBody>)Read(carriage, "_bodies");
                var convoy = Activator.CreateInstance(convoyType, true);
                convoyType.GetField("Car").SetValue(convoy, car);
                convoyType.GetField("Carriage").SetValue(convoy, carriage);
                var record = new LivingCity.Police.Prisoner {
                    CharacterId = 77, CaseId = 84, Stage = LivingCity.Police.PrisonStage.InTransit,
                    Leg = LivingCity.Police.PrisonLeg.Court
                };
                ((IList)convoyType.GetField("Riders").GetValue(convoy)).Add(record);
                var forward = Quaternion.Euler(0f, repetition * 72f, 0f) * Vector3.forward;
                var origin = prisoner.Tf.position;
                void Move(float distance)
                {
                    var at = origin + forward * distance;
                    position.SetValue(car, at); car.Tf.position = at;
                }
                void Start(float now) => begin.Invoke(null, new object[] { convoy, now });
                bool Alive(float now) => (bool)within.Invoke(null, new object[] { convoy, now });
                try
                {
                    Move(0f);
                    var men = new[] { prisoner, escort.Boss, escort.Hoods[0] };
                    for (int i = 0; i < men.Length; i++)
                        seats.Add(PrisonerCarriage.SeatBody(car.Tf, men[i], i == 0 ? 2 : i - 1, i == 0, null));
                    Start(0f);
                    for (int step = 1; step <= 17; step++)
                    {
                        Move(step * 10f);
                        Require(Alive(step * 100f), "a progressing transfer expired at the old five-minute deadline");
                    }
                    Require(record.Stage == LivingCity.Police.PrisonStage.InTransit && record.CaseId == 84 &&
                        ReferenceEquals(carriage.Car, car) && ReferenceEquals(carriage.Prisoner, prisoner) &&
                        Array.TrueForAll(men, man => man.Tf.parent == car.Tf),
                        "extending a moving trip changed its custody record or left a rider behind");
                    Move(180f);
                    Require(!Alive(1800.1f), "a moving but endless drive escaped its absolute ceiling");
                    Start(2000f);
                    Require(!Alive(2300.1f), "a stationary transfer lost its finite stall deadline");
                    Move(0f); Start(3000f);
                    for (int step = 1; step <= 2; step++)
                    {
                        Move(step % 2 == 0 ? -.5f : .5f);
                        Require(Alive(3000f + step * 100f), "short waits expired before their allowance");
                    }
                    Require(!Alive(3300.1f), "sub-metre jitter kept a stalled transfer alive indefinitely");
                    Start(9000f); Move(10f);
                    Require(Alive(9200f), "resuming after a deliberate roadblock retained an expired travel ceiling");
                }
                finally { PrisonerCarriage.RestoreBodies(seats, car.Position); RetireCar(car); }
            }
        }
    }
}
