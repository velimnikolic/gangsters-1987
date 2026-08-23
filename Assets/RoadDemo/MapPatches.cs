using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// A field of flat coloured rectangles in ONE mesh: the map's countryside.
    ///
    /// The island around the city is a heightfield, and drawing it the way the rest
    /// of the plan is drawn - an Image per patch - would cost thousands of objects
    /// for ground nobody clicks. This is a single Graphic instead: the caller hands
    /// it rectangles in MAP METRES (the same coordinates the plan's rects are laid
    /// on, measured from the map's origin) and it writes them into one mesh, one
    /// draw call, generated once and thereafter only scaled by the map's zoom.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MapPatches : MaskableGraphic
    {
        readonly List<(Rect area, Color32 tint)> _patches = new List<(Rect, Color32)>();

        /// <summary>Lines with a width: a quarter's streets, which are laid in the
        /// district's own frame and so need not run along the map's axes.</summary>
        readonly List<(Vector2 a, Vector2 b, float half, Color32 tint)> _strips =
            new List<(Vector2, Vector2, float, Color32)>();

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void Clear()
        {
            _patches.Clear();
            _strips.Clear();
            SetVerticesDirty();
        }

        /// <summary>Adds one rectangle, in map metres relative to this rect's centre.</summary>
        public void Add(Rect area, Color tint)
        {
            _patches.Add((area, tint));
            SetVerticesDirty();
        }

        /// <summary>Adds one line of a given width, in the same coordinates - a street
        /// that runs at whatever angle its quarter was laid at. Ends are square: the
        /// streets of a quarter meet at junctions and a rounded cap would print a
        /// notch at every corner.</summary>
        public void Add(Vector2 from, Vector2 to, float half, Color tint)
        {
            var along = to - from;
            if (along.sqrMagnitude < 1e-6f) return;
            _strips.Add((from, to, Mathf.Max(0.2f, half), tint));
            SetVerticesDirty();
        }

        public int Count => _patches.Count + _strips.Count;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var uv = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < _patches.Count; i++)
            {
                var (area, tint) = _patches[i];
                int at = vh.currentVertCount;
                vh.AddVert(new Vector3(area.xMin, area.yMin), tint, uv);
                vh.AddVert(new Vector3(area.xMin, area.yMax), tint, uv);
                vh.AddVert(new Vector3(area.xMax, area.yMax), tint, uv);
                vh.AddVert(new Vector3(area.xMax, area.yMin), tint, uv);
                vh.AddTriangle(at, at + 1, at + 2);
                vh.AddTriangle(at + 2, at + 3, at);
            }

            for (int i = 0; i < _strips.Count; i++)
            {
                var (a, b, half, tint) = _strips[i];
                var along = (b - a).normalized;
                var side = new Vector2(-along.y, along.x) * half;
                int at = vh.currentVertCount;
                vh.AddVert(new Vector3(a.x - side.x, a.y - side.y), tint, uv);
                vh.AddVert(new Vector3(a.x + side.x, a.y + side.y), tint, uv);
                vh.AddVert(new Vector3(b.x + side.x, b.y + side.y), tint, uv);
                vh.AddVert(new Vector3(b.x - side.x, b.y - side.y), tint, uv);
                vh.AddTriangle(at, at + 1, at + 2);
                vh.AddTriangle(at + 2, at + 3, at);
            }
        }
    }
}
