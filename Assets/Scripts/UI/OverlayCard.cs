using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>
    /// Where a floating card sits over the city, and when it has no business being on the
    /// screen at all.
    ///
    /// Every card in the game hangs off something in the world - a lieutenant's head, a
    /// patrol car's roof, a shop's door - and four of them answered that with the same
    /// five lines: project the anchor, then Mathf.Clamp the card into the viewport. The
    /// clamp is the lie. Pan the camera away and the subject leaves the screen, but the
    /// card stays, pinned to the edge and sliding along it as the city scrolls
    /// underneath: the player reads a card that follows THEM around rather than one that
    /// belongs to anything in the street.
    ///
    /// So the rule here: a card lives only while its anchor is inside the viewport, and
    /// dies the moment the anchor leaves it - the selection stands, the card simply is
    /// not drawn, and it comes back when the camera comes back. On screen it sits
    /// squarely over the anchor, nudged in only far enough that its own width does not
    /// hang off an edge, which is half a card at the very worst and only ever for a
    /// subject standing at the rim of the view.
    /// </summary>
    public static class OverlayCard
    {
        /// <summary>
        /// Screen point for a card's foot, or false when the anchor is not on the screen.
        /// Sizes and the lift are in the SAME units as the screen size handed in, so a
        /// caller on a scaled canvas multiplies its reference pixels by the scale factor
        /// before calling.
        /// </summary>
        public static bool TryPlace(
            Camera cam, Vector3 anchor, float lift, Vector2 size,
            float screenWidth, float screenHeight, out Vector3 position)
        {
            position = Vector3.zero;
            if (cam == null)
                return false;

            var screen = cam.WorldToScreenPoint(anchor);
            if (!OnScreen(screen, screenWidth, screenHeight))
                return false;

            var half = size.x * 0.5f;
            position = new Vector3(
                Mathf.Clamp(screen.x, half, Mathf.Max(half, screenWidth - half)),
                Mathf.Clamp(screen.y + lift, 0f, Mathf.Max(0f, screenHeight - size.y)),
                0f);
            return true;
        }

        /// <summary>
        /// Is a projected point in front of the camera and inside the view? z is the
        /// metres in front of the lens: behind the camera it goes negative and
        /// WorldToScreenPoint mirrors the point back onto the screen, which is how a card
        /// for something behind the player ends up drawn in front of them.
        /// </summary>
        public static bool OnScreen(Vector3 screen, float width, float height) =>
            screen.z > 0f &&
            screen.x >= 0f && screen.x <= width &&
            screen.y >= 0f && screen.y <= height;
    }
}
