using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    /// <summary>The public-side parking and road joins, in the district's own frame.</summary>
    public static class AirportLandsidePlan
    {
        public const float SpurVerge = 15f;
        public const float WalkWidth = 3f;
        public const float RoadHalf = CoreRoads.Cell;
        public static float LoopEdge => AirportSpec.LoopBackZ + AirportSpec.LoopRoadHalf;
        public static float StreetEdge => AirportSpec.StreetZ - RoadHalf;
        public static readonly float[] Crossings = { -30f, 30f };

        public sealed class Lot
        {
            public readonly string Name;
            public readonly Rect Bounds, Driveway;
            public readonly ParkingBlockPlan Parking;
            public float GateX => Bounds.center.x;
            public Lot(string name, float x0, float x1)
            {
                Name = name;
                Bounds = Rect.MinMaxRect(x0, AirportSpec.ParkZ0, x1, AirportSpec.ParkZ1);
                Parking = ParkingBlockPlan.Generate(Bounds.width, Bounds.height);
                float half = ParkingBlockPlan.GateWidth * 0.5f;
                Driveway = Rect.MinMaxRect(GateX - half, LoopEdge, GateX + half, Bounds.yMin);
            }
        }

        public static Lot[] Lots() => new[]
        {
            new Lot("West lot", AirportSpec.ParkX0, AirportSpec.ApproachX - SpurVerge),
            new Lot("East lot", AirportSpec.ApproachX + SpurVerge, AirportSpec.ParkX1),
        };

        public static Rect[] GateRoads() => new[]
        {
            GateRoad(AirportSpec.GaGateX), GateRoad(AirportSpec.CargoGateX),
        };

        static Rect GateRoad(float x) => Rect.MinMaxRect(x - RoadHalf,
            AirportSpec.ServiceRoadZ - AirportSpec.ServiceRoadWidth * 0.5f,
            x + RoadHalf, StreetEdge);

        /// <summary>Disjoint rectangles: a surface ends at a road or walk instead
        /// of hiding another surface a few millimetres below it.</summary>
        public static List<Rect> Subtract(Rect surface, IEnumerable<Rect> cuts)
        {
            var pieces = new List<Rect> { surface };
            foreach (var cut in cuts)
            {
                var next = new List<Rect>();
                foreach (var p in pieces)
                {
                    if (!p.Overlaps(cut)) { next.Add(p); continue; }
                    float x0 = Mathf.Max(p.xMin, cut.xMin), x1 = Mathf.Min(p.xMax, cut.xMax);
                    float z0 = Mathf.Max(p.yMin, cut.yMin), z1 = Mathf.Min(p.yMax, cut.yMax);
                    void Add(float a, float b, float c, float d)
                    {
                        if (b - a > 0.001f && d - c > 0.001f) next.Add(Rect.MinMaxRect(a, c, b, d));
                    }
                    Add(p.xMin, x0, p.yMin, p.yMax);
                    Add(x1, p.xMax, p.yMin, p.yMax);
                    Add(x0, x1, p.yMin, z0);
                    Add(x0, x1, z1, p.yMax);
                }
                pieces = next;
            }
            return pieces;
        }
    }
}
