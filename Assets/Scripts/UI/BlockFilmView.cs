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
    /// component owns are the pointer and the marks. Dragging turns the lens round the
    /// block and tilts it; the pointer over a building names it; a click on a building
    /// picks that premise. None of the three repaints the sheet except the pick, because
    /// the sheet is destroyed and rebuilt whole and a drag through it would delete the
    /// picture mid-drag.
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

        public const float PlanTilt = 88f;
        public const float IsoTilt = 52f;
        public const float StreetTilt = 14f;

        float yaw = -35f;
        float tilt = IsoTilt;

        /// <summary>Answered when the pointer moves onto a premise or off every one.
        /// The sheet writes a caption under it; it does not repaint.</summary>
        public System.Action<int> Hovered;

        /// <summary>Answered when a premise is picked, or -1 for the bare street.</summary>
        public System.Action<int> Picked;

        /// <summary>Answered whenever the block is turned, so the angle outlives the
        /// repaint a pick causes.</summary>
        public System.Action<float, float> Turned;

        /// <summary>Where the lens is standing, so the sheet can light the right key.</summary>
        public string Angle => tilt > 75f ? "PLAN" : tilt < 26f ? "STREET" : "ISO";

        public void Watch(BlockFilm crew, List<Door> read, float readYaw, float readTilt)
        {
            current = this;
            film = crew;
            doors.Clear();
            if (read != null)
                doors.AddRange(read);
            yaw = readYaw;
            tilt = Mathf.Clamp(readTilt, 8f, 89f);
            hovered = -1;
            BuildMarks();
        }

        public void Turn(float newYaw, float newTilt)
        {
            yaw = newYaw;
            tilt = Mathf.Clamp(newTilt, 8f, 89f);
            Turned?.Invoke(yaw, tilt);
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

        public void OnDrag(PointerEventData eventData) =>
            Turn(yaw - eventData.delta.x * 0.35f, tilt + eventData.delta.y * 0.3f);

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
