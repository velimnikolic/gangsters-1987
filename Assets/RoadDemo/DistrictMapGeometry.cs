using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public interface IDistrictMapSource
    {
        DistrictMapGeometry MapGeometry { get; }
    }

    /// <summary>Cartographic geometry published by the same calls that compose a fixed
    /// district. Coordinates are transformed once, before the survey worker reads them.
    /// This is a view description, never a source of obstacles or business state.</summary>
    public sealed class DistrictMapGeometry
    {
        public readonly struct Surface
        {
            public readonly Rect World;
            public readonly Vector2 A, B, C;
            public readonly bool Triangle;
            public readonly Color32 Ink;
            public readonly float Height;

            public Surface(Rect world, Color32 ink, float height)
            {
                World = world; Ink = ink; Height = height;
                A = B = C = default; Triangle = false;
            }

            public Surface(Vector3 a, Vector3 b, Vector3 c, Color32 ink)
            {
                A = new Vector2(a.x, a.z); B = new Vector2(b.x, b.z);
                C = new Vector2(c.x, c.z); Ink = ink; Height = a.y;
                World = Rect.MinMaxRect(Mathf.Min(A.x, B.x, C.x), Mathf.Min(A.y, B.y, C.y),
                    Mathf.Max(A.x, B.x, C.x), Mathf.Max(A.y, B.y, C.y));
                Triangle = true;
            }
        }

        public readonly struct Building
        {
            public readonly Bounds Bounds;
            public readonly Transform View;
            public readonly string Name;
            public readonly TurfType Type;

            public Building(Bounds bounds, Transform view, string name, TurfType type)
            { Bounds = bounds; View = view; Name = name; Type = type; }
        }

        readonly List<Surface> _surfaces = new List<Surface>();
        readonly List<Building> _buildings = new List<Building>();
        DistrictFrame _frame;
        public IReadOnlyList<Surface> Surfaces => _surfaces;
        public IReadOnlyList<Building> Buildings => _buildings;

        public void Reset(DistrictFrame frame)
        { Clear(); _frame = frame; }

        public void Clear()
        { _surfaces.Clear(); _buildings.Clear(); }

        public void Fill(Rect local, Color32 ink, float height)
        {
            if (local.width <= 0f || local.height <= 0f) return;
            _surfaces.Add(new Surface(_frame.ToWorldRect(local), ink, height + _frame.origin.y));
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color32 ink)
            => _surfaces.Add(new Surface(_frame.ToWorld(a), _frame.ToWorld(b), _frame.ToWorld(c), ink));

        public void Mesh(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles, Color32 ink)
        {
            for (int i = 0; i + 2 < triangles.Count; i += 3)
                Triangle(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]], ink);
        }

        /// <summary>Called after placement; preserves the physical building's footprint
        /// for picking and its view for the existing prepared massing collector.</summary>
        public void AddBuilding(Bounds world, Transform view, string name, TurfType type)
            => _buildings.Add(new Building(world, view, name, type));
    }
}
