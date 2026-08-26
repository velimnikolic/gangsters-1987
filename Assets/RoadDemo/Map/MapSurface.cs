using UnityEngine;
using UnityEngine.EventSystems;

namespace RoadDemo
{
    /// <summary>
    /// The pointer half of the map: the one place a screen position becomes a RASTER
    /// pixel, which is the coordinate every piece of hit testing on this map works in.
    ///
    /// The handoff is explicit about this and it matters: a building is one or two
    /// pixels, a man is one, and the tolerances that make either of them clickable
    /// (half a pixel round a footprint, two and a half by three round a figure) only
    /// mean anything in raster space. Converting to metres first and testing there would
    /// make the same click behave differently at every zoom.
    ///
    /// Everything else is left to the map. This reports presses, drags and hovers and
    /// forms no opinion about any of them.
    /// </summary>
    public sealed class MapSurface : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerMoveHandler, IPointerExitHandler, IScrollHandler
    {
        public interface IReader
        {
            void MapPress(Vector2 raster, PointerEventData.InputButton button);
            void MapRelease(Vector2 raster, PointerEventData.InputButton button);
            void MapDrag(Vector2 raster, bool ended);
            void MapHover(Vector2 raster, bool over);
            void MapScroll(Vector2 raster, float delta);
        }

        public IReader Reader;

        RectTransform _rect;

        void Awake() => _rect = (RectTransform)transform;

        /// <summary>Client space to raster space. The handoff's formula, in the
        /// coordinates uGUI actually offers: a local point inside the rect, turned into
        /// a fraction of it, times 320 by 200 - and flipped, because a raster runs down
        /// the page while a rect runs up it.</summary>
        public bool ToRaster(PointerEventData eventData, out Vector2 raster)
        {
            raster = Vector2.zero;
            if (_rect == null)
                return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rect, eventData.position, eventData.pressEventCamera, out var local))
                return false;

            var size = _rect.rect.size;
            if (size.x <= 0f || size.y <= 0f)
                return false;

            var u = (local.x - _rect.rect.xMin) / size.x;
            var v = (local.y - _rect.rect.yMin) / size.y;
            // AUTHORED units, not real pixels. A footprint is one or two authored
            // units across and the tolerances that make it clickable are properties of
            // the layout, not of how many real pixels it happens to be drawn in.
            raster = new Vector2(u * MapRaster.AW, (1f - v) * MapRaster.AH);
            return true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Reader != null && ToRaster(eventData, out var raster))
                Reader.MapPress(raster, eventData.button);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (Reader != null && ToRaster(eventData, out var raster))
                Reader.MapRelease(raster, eventData.button);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Reader != null && ToRaster(eventData, out var raster))
                Reader.MapDrag(raster, false);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Reader != null && ToRaster(eventData, out var raster))
                Reader.MapDrag(raster, false);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Reader != null && ToRaster(eventData, out var raster))
                Reader.MapDrag(raster, true);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (Reader != null && ToRaster(eventData, out var raster))
                Reader.MapHover(raster, true);
        }

        public void OnPointerExit(PointerEventData eventData) =>
            Reader?.MapHover(Vector2.zero, false);

        public void OnScroll(PointerEventData eventData)
        {
            if (Reader != null && ToRaster(eventData, out var raster))
                Reader.MapScroll(raster, eventData.scrollDelta.y);
        }
    }
}
