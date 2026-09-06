using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Spatially indexed road earthworks. Only changes terrain, never road/actor state.</summary>
    public sealed class IslandRoadCorridors
    {
        const float Cell = 160f, Shoulder = 100f;
        struct Segment { public Vector2 A, B; public float Half, Ceiling, MeshPadding; }
        readonly Dictionary<(int, int), List<Segment>> _cells = new Dictionary<(int, int), List<Segment>>();

        public void Add(RoadLine line, float half, Func<float, float> surface,
            float bed = RoadDemoBuilder.RoadBed, float meshPadding = 0f)
        {
            for (float s = 0f; s < line.Length; s += 24f)
            {
                float end = Mathf.Min(s + 24f, line.Length);
                var a = line.PointAt(s); var b = line.PointAt(end);
                var segment = new Segment { A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z), Half = half,
                    MeshPadding = meshPadding, Ceiling = Mathf.Max(bed, Mathf.Min(surface(s), surface(end)) - 6.5f) };
                float pad = half + Shoulder + meshPadding;
                int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - pad) / Cell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + pad) / Cell);
                int z0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - pad) / Cell), z1 = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + pad) / Cell);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    if (!_cells.TryGetValue((x, z), out var list)) _cells[(x, z)] = list = new List<Segment>();
                    list.Add(segment);
                }
            }
        }

        public float Shape(float x, float z, float height, out bool road)
        {
            road = false;
            if (!_cells.TryGetValue((Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out var list)) return height;
            var point = new Vector2(x, z);
            float original = height;
            foreach (var segment in list)
            {
                var ab = segment.B - segment.A;
                float t = Mathf.Clamp01(Vector2.Dot(point - segment.A, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                float d = Vector2.Distance(point, segment.A + ab * t) - segment.Half;
                road |= d < 14f;
                d -= segment.MeshPadding; // Mesh clearance does not widen the vegetation exclusion.
                if (d >= Shoulder) continue;
                float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, d) / Shoulder);
                height = Mathf.Min(height, Mathf.Lerp(original, Mathf.Min(original, segment.Ceiling), weight));
            }
            return height;
        }
    }
}
