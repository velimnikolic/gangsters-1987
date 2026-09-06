using System;

namespace RoadDemo
{
    /// <summary>Ground-relative camera geometry, independent of streamed terrain views.</summary>
    public static class CameraGrounding
    {
        public static float HeightForBoom(float boom, float pitch) =>
            boom * (float)Math.Sin(pitch * Math.PI / 180.0);

        public static float BoomForHeight(float height, float pitch) =>
            height / Math.Max(0.01f, HeightForBoom(1f, pitch));

        public static float FocusHeight(float requested, float ground, bool riding) =>
            riding ? Math.Max(requested, ground) : ground;

        /// <summary>Rise immediately to clear terrain; ease down without ever going
        /// below the required height. Damping ascent could put the camera in a hill.</summary>
        public static float SettleHeight(float current, float required, float seconds) =>
            required >= current ? required :
                required + (current - required) * (float)Math.Exp(-8f * Math.Max(0f, seconds));

        /// <summary>Keep the requested height above both the focus ground and the
        /// ground below the lens. On an uphill orbit the lens must clear the slope
        /// even when the focus remains downhill. This lift never changes zoom.</summary>
        public static float LensHeight(float focusHeight, float groundBelowLens, float boomHeight) =>
            Math.Max(focusHeight, groundBelowLens) + boomHeight;
    }
}
