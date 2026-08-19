using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>A junction of the lane graph: where carriageways meet (or one ends).
    /// Its box is the tarmac of the crossing; lanes stop at the box edge and the
    /// CONNECTORS (LaneNet) carry cars across it. What is inside the box at any
    /// moment is in <see cref="Inside"/>, and that - with the connectors' conflict
    /// table - is what keeps two cars from meeting in the middle of it.</summary>
    public class RoadNode
    {
        public int I, J;
        public float X, Z;
        public float XMin, XMax, ZMin, ZMax;
        public TrafficSignal Signal;
        public readonly List<RoadEdge> Incoming = new List<RoadEdge>();
        public readonly List<RoadEdge> Outgoing = new List<RoadEdge>();

        /// <summary>Every way across this box, one per (incoming lane, outgoing lane)
        /// pair a car may take - built by <see cref="LaneNet.Finish"/>.</summary>
        public readonly List<Connector> Connectors = new List<Connector>();

        /// <summary>The cars in the box right now, and the connector each is on.</summary>
        public readonly List<NodeOccupant> Inside = new List<NodeOccupant>();

        /// <summary>Metres short of the box edge a car's nose stops at (the zebra
        /// band and a little air in the city; next to nothing on a plain corner).</summary>
        public float StopSetback = 5.7f;

        public Vector3 Centre => new Vector3(X, 0f, Z);

        /// <summary>The connector from this incoming lane to that outgoing one, or null.</summary>
        public Connector ConnectorFor(RoadEdge from, RoadEdge to)
        {
            for (int i = 0; i < Connectors.Count; i++)
                if (Connectors[i].From == from && Connectors[i].To == to) return Connectors[i];
            return null;
        }
    }

    /// <summary>One lane, one way, between two nodes: a straight run of the lane
    /// graph the traffic routes over. It lies on a <see cref="Carriageway"/> (the
    /// whole road between those nodes, both ways) at a lateral offset; a car drives
    /// the carriageway in (s, d) coordinates and merely BELONGS to a lane. Edges
    /// built without a carriageway (a cul-de-sac bulb) get a one-lane one of their
    /// own (LaneNet.Adopt), so every edge can answer the same questions.</summary>
    public class RoadEdge
    {
        public RoadNode From, To;
        public Vector3 Start, End;
        public Vector3 Dir;
        public float Length;
        public bool NorthSouth;
        public float SpeedLimit;

        /// <summary>The road this lane lies on.</summary>
        public Carriageway Road;
        /// <summary>Lateral position on the road (metres right of its axis).</summary>
        public float Offset;
        /// <summary>+1: runs with the road's axis (A to B); -1: against it.</summary>
        public int Heading = 1;
        /// <summary>Road-s of this lane's Start.</summary>
        public float S0;

        /// <summary>Progress along this lane to road-s.</summary>
        public float RoadS(float progress) => S0 + Heading * progress;
        /// <summary>Road-s to progress along this lane.</summary>
        public float Progress(float roadS) => Heading * (roadS - S0);

        /// <summary>The cars that belong to this lane right now (the one they keep
        /// to, whatever the manoeuvre in hand): what a parked patrol car reads to
        /// see if its kerb is clear, and the audio to count traffic.</summary>
        public readonly List<RoadCar> Cars = new List<RoadCar>();
    }
}
