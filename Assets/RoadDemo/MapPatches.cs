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

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void Clear()
        {
            _patches.Clear();
            SetVerticesDirty();
        }

        /// <summary>Adds one rectangle, in map metres relative to this rect's centre.</summary>
        public void Add(Rect area, Color tint)
        {
            _patches.Add((area, tint));
            SetVerticesDirty();
        }

        public int Count => _patches.Count;

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
        }
    }
}
