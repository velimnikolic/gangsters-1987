using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// The picture of the block on the block file: a live frame off <see cref="BlockFilm"/>
    /// with the outfit's marks hung over the doors on it.
    ///
    /// It is a photograph of the city, not a drawing of one, so the only things this
    /// component owns are the pointer, the marks and the CUT. Dragging turns the lens
    /// round the block - only round it: the angle off the ground is the city's own and
    /// there is nothing to tilt. The pointer over a building names it; a click on a
    /// building picks that premise. None of the three repaints the sheet except the pick,
    /// because the sheet is destroyed and rebuilt whole and a drag through it would
    /// delete the picture mid-drag.
    ///
    /// The cut is what makes it a block and not a view of a city: the frame is drawn only
    /// inside the block's own silhouette - its plot and kerb pushed up to the rooflines,
    /// projected through the same lens - so the plate carries one block standing on the
    /// dark, and nothing of the streets around it.
    ///
    /// The marks are what the design colours a building by ownership FOR: the render is
    /// the street's own paint, so whose door it is is said with a mark over the door
    /// rather than by repainting a building the player also sees from the car.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BlockFilmView : RawImage,
        IBeginDragHandler, IDragHandler, IPointerClickHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>One premise the file knows about, and where its door stands.</summary>
        public struct Door
        {
            public int Key;
            public Vector3 World;
            public Color Ink;
            public bool Picked;

            /// <summary>The collider roots this premise answers to, so a click on the
            /// building itself picks the same door as a click on its mark.</summary>
            public Transform View;
        }

        /// <summary>The picture the file is currently showing. The sheet is rebuilt by
        /// destroying it whole and building it again, and Unity destroys at the END of
        /// the frame - so the OLD picture's shutdown runs after the NEW one has already
        /// put the lens over the block. Only the current picture may switch it off.
        /// </summary>
        static BlockFilmView current;

        readonly List<Door> doors = new List<Door>();
        readonly List<RectTransform> marks = new List<RectTransform>();

        BlockFilm film;
        int hovered = -1;
        bool pointerInside;
        bool dragged;

        float yaw = -35f;

        /// <summary>The block's silhouette in the picture's own 0..1 coordinates, and the
        /// one it was last drawn at. Rebuilt as the block turns and only then.</summary>
        readonly List<Vector2> hull = new List<Vector2>();
        readonly List<Vector2> drawn = new List<Vector2>();
        readonly List<Vector2> scratch = new List<Vector2>();

        /// <summary>Answered when the pointer moves onto a premise or off every one.
        /// The sheet writes a caption under it; it does not repaint.</summary>
        public System.Action<int> Hovered;

        /// <summary>Answered when a premise is picked, or -1 for the bare street.</summary>
        public System.Action<int> Picked;

        /// <summary>Answered whenever the block is turned, so the angle outlives the
        /// repaint a pick causes.</summary>
        public System.Action<float> Turned;

        /// <summary>Where the block is standing, in degrees round it.</summary>
        public float Yaw => yaw;

        public void Watch(BlockFilm crew, List<Door> read, float readYaw)
        {
            current = this;
            film = crew;
            doors.Clear();
            if (read != null)
                doors.AddRange(read);
            yaw = readYaw;
            hovered = -1;
            BuildMarks();
            Cut();
        }

        public void Turn(float newYaw)
        {
            yaw = Mathf.Repeat(newYaw, 360f);
            Turned?.Invoke(yaw);
            Cut();
        }

        // -------------------------------------------------------------------- marks

        /// <summary>A small square in the ownership colour per door, and a ring round the
        /// one that is picked. Built once per repaint and only MOVED afterwards, so a
        /// turning block does not allocate a mark a frame.</summary>
        void BuildMarks()
        {
            for (var i = 0; i < marks.Count; i++)
                if (marks[i] != null)
                    Destroy(marks[i].gameObject);
            marks.Clear();

            for (var i = 0; i < doors.Count; i++)
            {
                var mark = new GameObject("Door mark", typeof(RectTransform));
                mark.transform.SetParent(transform, false);
                var rect = (RectTransform)mark.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                var size = doors[i].Picked ? 15f : 10f;
                rect.sizeDelta = new Vector2(size, size);

                var face = mark.AddComponent<Image>();
                face.color = doors[i].Ink;
                face.raycastTarget = false;

                if (doors[i].Picked)
                {
                    var ring = new GameObject("Ring", typeof(RectTransform));
                    ring.transform.SetParent(rect, false);
                    var ringRect = (RectTransform)ring.transform;
                    ringRect.anchorMin = Vector2.zero;
                    ringRect.anchorMax = Vector2.one;
                    ringRect.offsetMin = new Vector2(-5f, -5f);
                    ringRect.offsetMax = new Vector2(5f, 5f);
                    LedgerKit.Frame(ringRect, 2f, LedgerV2.HeadCream);
                }
                marks.Add(rect);
            }
        }

        void PlaceMarks()
        {
            if (film == null)
                return;
            var size = rectTransform.rect.size;
            for (var i = 0; i < marks.Count && i < doors.Count; i++)
            {
                var mark = marks[i];
                if (mark == null)
                    continue;
                if (!film.TryPlace(doors[i].World, out var viewport))
                {
                    mark.gameObject.SetActive(false);
                    continue;
                }
                mark.gameObject.SetActive(true);
                mark.anchoredPosition = new Vector2(viewport.x * size.x, viewport.y * size.y);
            }
        }

        // ---------------------------------------------------------------------- the cut

        static readonly System.Comparison<Vector2> LeftToRight =
            (a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);

        /// <summary>
        /// The block's silhouette in the picture: the eight corners of the film's volume,
        /// projected through the film's own lens and wrapped in a convex hull. Worked out
        /// every frame because the lens may have moved, but the mesh is rebuilt only when
        /// the shape actually changed - a block standing still costs nothing.
        /// </summary>
        void Cut()
        {
            if (film == null)
                return;
            var box = film.Volume;
            scratch.Clear();
            if (box.size.sqrMagnitude > 0.001f)
            {
                var min = box.min;
                var max = box.max;
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? min.x : max.x,
                        (i & 2) == 0 ? min.y : max.y,
                        (i & 4) == 0 ? min.z : max.z);
                    // A corner behind the lens leaves no honest shape: show the whole
                    // frame rather than a wrong silhouette.
                    if (!film.TryPlace(corner, out var point))
                    {
                        scratch.Clear();
                        break;
                    }
                    scratch.Add(new Vector2(
                        Mathf.Clamp01(point.x), Mathf.Clamp01(point.y)));
                }
            }

            Wrap(scratch, hull);
            if (Same(hull, drawn))
                return;
            drawn.Clear();
            drawn.AddRange(hull);
            SetVerticesDirty();
        }

        /// <summary>The frame is drawn only inside the silhouette. Outside it the plate's
        /// own dark stands, which is what makes the picture a block on a table rather
        /// than a window onto the city.</summary>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            if (drawn.Count < 3)
            {
                base.OnPopulateMesh(vh);
                return;
            }

            vh.Clear();
            var rect = rectTransform.rect;
            var uv = uvRect;
            for (var i = 0; i < drawn.Count; i++)
            {
                var point = drawn[i];
                vh.AddVert(
                    new Vector3(rect.xMin + point.x * rect.width,
                                rect.yMin + point.y * rect.height),
                    color,
                    new Vector2(uv.x + point.x * uv.width, uv.y + point.y * uv.height));
            }
            for (var i = 2; i < drawn.Count; i++)
                vh.AddTriangle(0, i - 1, i);
        }

        /// <summary>Andrew's monotone chain over at most eight points.</summary>
        static void Wrap(List<Vector2> points, List<Vector2> into)
        {
            into.Clear();
            if (points.Count < 3)
                return;
            points.Sort(LeftToRight);

            for (var pass = 0; pass < 2; pass++)
            {
                var start = into.Count;
                for (var i = 0; i < points.Count; i++)
                {
                    var point = pass == 0 ? points[i] : points[points.Count - 1 - i];
                    while (into.Count - start >= 2 &&
                           Turns(into[into.Count - 2], into[into.Count - 1], point) <= 0f)
                        into.RemoveAt(into.Count - 1);
                    into.Add(point);
                }
                // The chain's last point is the other chain's first.
                into.RemoveAt(into.Count - 1);
            }
            if (into.Count < 3)
                into.Clear();
        }

        static float Turns(Vector2 a, Vector2 b, Vector2 c)
            => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static bool Same(List<Vector2> a, List<Vector2> b)
        {
            if (a.Count != b.Count)
                return false;
            for (var i = 0; i < a.Count; i++)
                if ((a[i] - b[i]).sqrMagnitude > 0.000004f)
                    return false;
            return true;
        }

        // ------------------------------------------------------------------ pointer

        public void OnPointerEnter(PointerEventData eventData) => pointerInside = true;

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            if (hovered < 0)
                return;
            hovered = -1;
            Hovered?.Invoke(-1);
        }

        public void OnBeginDrag(PointerEventData eventData) => dragged = true;

        /// <summary>Left and right turn the block. Up and down do nothing on purpose:
        /// the block is seen at the city's own angle and the book does not offer a
        /// second one.</summary>
        public void OnDrag(PointerEventData eventData) =>
            Turn(yaw - eventData.delta.x * 0.35f);

        public void OnPointerClick(PointerEventData eventData)
        {
            // A turn that happens to end over a door is a turn, not a pick.
            if (dragged)
            {
                dragged = false;
                return;
            }
            Picked?.Invoke(At(eventData.position, eventData.pressEventCamera));
        }

        /// <summary>The picture leaving the screen is the file leaving it - a page
        /// turned, a book shut, a card closed. The lens goes off and the city gets its
        /// ground back here, so no path can forget to do it.</summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            if (current != this)
                return;
            current = null;
            RoadDemo.CityBlockRecycler.Release();
            BlockFilm.StopIfRunning();
        }

        void LateUpdate()
        {
            PlaceMarks();
            Cut();
            if (!pointerInside)
                return;
            var mouse = Mouse.current;
            if (mouse == null)
                return;
            var found = At(mouse.position.ReadValue(), null);
            if (found == hovered)
                return;
            hovered = found;
            Hovered?.Invoke(found);
        }

        /// <summary>
        /// Which premise is under a screen point. The ray goes through the film's own
        /// lens into the real city, so what answers is the building the player would have
        /// clicked from the street. When the ray lands on something no premise owns -
        /// pavement, a parked car, a lamp - the nearest door mark takes the click, which
        /// is what makes a small shopfront on a long block pickable at all.
        /// </summary>
        int At(Vector2 screen, Camera pressCamera)
        {
            var canvas = GetComponentInParent<Canvas>();
            var camera = pressCamera != null ? pressCamera
                : canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;
            if (film == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screen, camera, out var local))
                return -1;

            var rect = rectTransform.rect;
            var viewport = new Vector2(
                Mathf.InverseLerp(rect.xMin, rect.xMax, local.x),
                Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));

            if (film.TryPick(viewport, out var hit))
            {
                for (var i = 0; i < doors.Count; i++)
                {
                    var view = doors[i].View;
                    if (view != null && hit.transform.IsChildOf(view))
                        return doors[i].Key;
                }
            }

            // Nothing owned by a premise answered: fall back to the mark nearest the
            // pointer, but only when the pointer is genuinely on it.
            var best = -1;
            var bestDistance = 22f;
            var size = rect.size;
            var point = new Vector2(viewport.x * size.x, viewport.y * size.y);
            for (var i = 0; i < marks.Count && i < doors.Count; i++)
            {
                if (marks[i] == null || !marks[i].gameObject.activeSelf)
                    continue;
                var distance = Vector2.Distance(marks[i].anchoredPosition, point);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                best = doors[i].Key;
            }
            return best;
        }
    }
}
