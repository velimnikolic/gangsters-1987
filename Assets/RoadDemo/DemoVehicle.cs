using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A car of the city's traffic on the lane graph: the RoadCar with the plain
    /// commuter at the wheel (DriverProfile.Traffic) - keeps its lane, follows,
    /// waits at the lights, wanders at the junctions, flinches at gunfire, swings a
    /// little over the crown round a car at the kerb, and only after a long wait
    /// behind a jam uses the far lane or turns round. The name and the members the
    /// rest of the demo reads (Spawn, Tick, CurrentEdge, Progress, Speed, HalfLen,
    /// HalfWide, PickNext / LimitTarget / Fearless for a derived driver) are the ones
    /// it always had; the driving underneath is the shared RoadCar's.
    /// </summary>
    public class DemoVehicle : RoadCar
    {
        public DemoVehicle()
        {
            Profile = DriverProfile.Traffic;
            Tag = "traffic";
        }

        protected override RoadEdge PickNext(RoadEdge straight, List<RoadEdge> lefts, List<RoadEdge> rights)
            => base.PickNext(straight, lefts, rights);

        protected override float LimitTarget(float target) => target;

        protected override bool Fearless => Profile.Fearless;
    }
}
