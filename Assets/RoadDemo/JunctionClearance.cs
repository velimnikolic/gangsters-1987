using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Body envelopes for junction admission. Shared by every priority check;
    /// the final movement guard still checks the actual next pose.</summary>
    internal static class JunctionClearance
    {
        internal sealed class Cache
        {
            internal readonly Dictionary<(Connector, float, float, float), Sweep> Shapes = new Dictionary<(Connector, float, float, float), Sweep>();
            internal readonly Dictionary<(Sweep, Sweep), bool> Results = new Dictionary<(Sweep, Sweep), bool>();
        }

        internal sealed class Sweep
        {
            internal Connector Connector;
            internal float Length, Width, Axle;
        }

        // Scratch geometry is reused, not retained for every prefab at every city node.
        static readonly List<Sample> samplesA = new List<Sample>(), samplesB = new List<Sample>();

        internal struct Sample
        {
            internal Vector3 Position, Forward;
            internal float Air;
        }

        internal static bool Conflicts(Connector a, RoadCar car, Connector b, RoadCar other)
        {
            if (a == null || b == null || car == null || other == null) return true;
            if (a.Node != b.Node) return false;
            if (a.From == b.From) return false; // following owns the common origin
            if (a.To == b.To || a.UTurn || b.UTurn) return true;
            // An off-lane merge does not follow the nominal connector envelope.
            if (Mathf.Abs(car.CrossingOffset) > .05f || Mathf.Abs(other.CrossingOffset) > .05f)
                return true;
            var cache = a.Node.BodyClearance ??= new Cache();
            if (cache.Shapes.Count >= 127) { cache.Shapes.Clear(); cache.Results.Clear(); }
            var ours = Shape(cache, a, car);
            var theirs = Shape(cache, b, other);
            if (cache.Results.TryGetValue((ours, theirs), out bool conflict)) return conflict;
            conflict = Intersects(ours, theirs);
            if (cache.Results.Count >= 512) cache.Results.Clear();
            cache.Results[(ours, theirs)] = cache.Results[(theirs, ours)] = conflict;
            return conflict;
        }

        static bool Intersects(Sweep a, Sweep b)
        {
            SampleEnvelope(a, samplesA); SampleEnvelope(b, samplesB);
            foreach (var pa in samplesA)
                foreach (var pb in samplesB)
                {
                    float reach = a.Length + a.Width + b.Length + b.Width + pa.Air + pb.Air;
                    if ((pa.Position - pb.Position).sqrMagnitude > reach * reach) continue;
                    if (RoadSpace.Overlap(pa.Position, pa.Forward, a.Length, a.Width,
                        pb.Position, pb.Forward, b.Length, b.Width, pa.Air + pb.Air, out _)) return true;
                }
            return false;
        }

        static Sweep Shape(Cache cache, Connector connector, RoadCar car)
        {
            var key = (connector, car.HalfLen, car.HalfWide, car.CrossingAxle);
            if (cache.Shapes.TryGetValue(key, out var shape)) return shape;
            shape = new Sweep { Connector = connector, Length = car.HalfLen, Width = car.HalfWide, Axle = car.CrossingAxle };
            cache.Shapes[key] = shape;
            return shape;
        }

        static void SampleEnvelope(Sweep shape, List<Sample> samples)
        {
            samples.Clear();
            var connector = shape.Connector;
            float start = -shape.Length;
            float end = connector.Length + shape.Length + shape.Axle;
            int steps = Mathf.CeilToInt((end - start) / .1f);
            float span = (end - start) / steps;
            for (int i = 0; i <= steps; i++)
            {
                float s = start + i * span;
                Pose(connector, s, shape.Axle, out var position, out var forward);
                Pose(connector, s + span * .5f, shape.Axle, out var next, out var facing);
                float corner = Vector3.Angle(forward, facing) * Mathf.Deg2Rad *
                    Mathf.Sqrt(shape.Length * shape.Length + shape.Width * shape.Width);
                samples.Add(new Sample { Position = position, Forward = forward,
                    Air = .08f + 2f * ((next - position).magnitude + corner) });
            }
        }

        internal static void Pose(Connector connector, float centre, float axleBack,
            out Vector3 position, out Vector3 forward)
        {
            float s = centre - axleBack;
            if (s < 0f) { forward = connector.From.Dir; position = connector.Pts[0] + forward * s; }
            else if (s > connector.Length)
            { forward = connector.To.Dir; position = connector.Pts[connector.Pts.Length - 1] + forward * (s - connector.Length); }
            else connector.Pose(s, out position, out forward);
            position += forward * axleBack;
        }
    }
}
