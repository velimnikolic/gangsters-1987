using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;
using LivingCity.Police;

static class Passengers
{
    static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    public static void Check()
    {
        SelectiveRemoval();
        HaltedCarriage(CarriageStage.Riding);
        HaltedCarriage(CarriageStage.Boarding);
        Console.WriteLine("Passenger lifecycle: 3/3 passed");
    }

    static CrewWalker Man(Transform root)
    {
        var man = new CrewWalker { Surrendered = true };
        man.Tf.SetParent(root, true);
        man.Tf.localScale = new Vector3(1.1f, 1.2f, 1.3f);
        man.Tf.Renderers = new[] { new Renderer(), new Renderer { enabled = false } };
        return man;
    }

    static void SelectiveRemoval()
    {
        var car = new PolicePatrolCar { Tf = new Transform { position = new Vector3(0f, 0f, 100f) } };
        var other = new Transform { position = new Vector3(0f, 0f, 300f) };
        var root = new Transform();
        var prisoner = Man(root);
        var escort = Man(root);
        var otherPrisoner = Man(root);
        var bodies = new List<PrisonerCarriage.SeatedBody> {
            PrisonerCarriage.SeatBody(car.Tf, prisoner, 2, true, null),
            PrisonerCarriage.SeatBody(other, otherPrisoner, 2, true, null),
            PrisonerCarriage.SeatBody(car.Tf, escort, 0, false, null) };
        Require(prisoner.Riding && escort.Riding && otherPrisoner.Riding, "seating failed");
        int removed = 0;
        UnityEngine.Object.Destroying = obj =>
        {
            if (obj != car.Tf.gameObject) return;
            Require(prisoner.Tf.parent != car.Tf && escort.Tf.parent != car.Tf,
                "destroyed vehicle still parents a real passenger");
            removed++;
        };
        try
        {
            PrisonerCarriage.RestoreBodies(bodies, Vector3.zero, atEachCarrier: true, onlyCarrier: car.Tf);
            car.Vanish();
            Require(removed == 1 && bodies.Count == 1 && bodies[0].Man == otherPrisoner,
                "selective restore discarded another carrier's passenger record");
            foreach (var man in new[] { prisoner, escort })
                Require(!man.Riding && man.Surrendered && !man.Dead && man.Tf.parent == root &&
                    (man.Tf.position - car.Tf.position).magnitude < 5f &&
                    man.Tf.Renderers[0].enabled && !man.Tf.Renderers[1].enabled &&
                    man.Tf.localScale == new Vector3(1.1f, 1.2f, 1.3f),
                    "unseated body lost its local position, scale, visibility or custody state");
            Require(otherPrisoner.Riding && otherPrisoner.Tf.parent == other &&
                !otherPrisoner.Tf.Renderers[0].enabled, "another moving carrier was unloaded");
            var at = prisoner.Tf.position;
            PrisonerCarriage.RestoreBodies(bodies, Vector3.zero, atEachCarrier: true, onlyCarrier: car.Tf);
            Require(prisoner.Tf.position == at && bodies.Count == 1, "repeat removal moved an unseated man");
            PrisonerCarriage.RestoreBodies(bodies, new Vector3(20f, 0f, 300f));
            Require(!otherPrisoner.Riding && bodies.Count == 0 && otherPrisoner.Tf.parent == root,
                "normal later arrival could not restore the remaining carrier");
        }
        finally { UnityEngine.Object.Destroying = null; }
    }

    static void HaltedCarriage(CarriageStage stage)
    {
        var car = new PolicePatrolCar { Tf = new Transform() };
        typeof(RoadCar).GetProperty("Speed").SetValue(car, 2f);
        var root = new Transform();
        var prisoner = Man(root);
        var officer = Man(root);
        var escort = new DemoCrews.Unit();
        escort.Men.Add(officer);
        var carriage = new PrisonerCarriage(1, prisoner, escort, car, new DemoCrews(), null);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var bodies = (List<PrisonerCarriage.SeatedBody>)typeof(PrisonerCarriage)
            .GetField("_bodies", flags).GetValue(carriage);
        bodies.Add(PrisonerCarriage.SeatBody(car.Tf, prisoner, 2, true, null));
        bodies.Add(PrisonerCarriage.SeatBody(car.Tf, officer, 0, false, null));
        typeof(PrisonerCarriage).GetField("_prisonerSeated", flags).SetValue(carriage, true);
        typeof(PrisonerCarriage).GetProperty("Stage").SetValue(carriage, stage);
        carriage.BeginHalt();
        Require(!carriage.DismountHalted(car.Position) && prisoner.Riding,
            "passengers dismounted before the carrier stopped");
        typeof(RoadCar).GetProperty("Speed").SetValue(car, 0f);
        Require(carriage.DismountHalted(car.Position) && !carriage.PrisonerSeated &&
            carriage.Stage == CarriageStage.Halted && prisoner.Surrendered &&
            !prisoner.Riding && !officer.Riding && officer.GunpointTarget == prisoner,
            "stopped carrier did not preserve its prisoner under the same escort");
        car.Vanish();
    }
}
