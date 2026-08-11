using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public class RoadNode
    {
        public int I, J;
        public float X, Z;
        public float XMin, XMax, ZMin, ZMax;
        public TrafficSignal Signal;
        public readonly List<RoadEdge> Incoming = new List<RoadEdge>();
        public readonly List<RoadEdge> Outgoing = new List<RoadEdge>();
    }

    public class RoadEdge
    {
        public RoadNode From, To;
        public Vector3 Start, End;
        public Vector3 Dir;
        public float Length;
        public bool NorthSouth;
        public float SpeedLimit;
        public readonly List<DemoVehicle> Cars = new List<DemoVehicle>();
    }
}
