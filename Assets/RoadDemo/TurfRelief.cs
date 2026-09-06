using UnityEngine;

namespace RoadDemo
{
    /// <summary>World-anchored height tint, hillshade and 25 m contours.</summary>
    public static class TurfRelief
    {
        /// <summary>Street lettering recedes at city scale, independently of the island zoom ceiling.</summary>
        public static float DetailRecession(float boom, float mapAt, float ceiling) =>
            Mathf.InverseLerp(mapAt, Mathf.Min(ceiling, Mathf.Max(440f, mapAt + 40f)), boom);

        public static Color32 At(TurfHeightField field, float x, float z, float metres, Color32 paper)
        {
            float height = field.At(x, z);
            if (height < 2f) return paper;
            float step = Mathf.Max(field.Step, metres);
            float east = field.At(x + step, z), west = field.At(x - step, z);
            float north = field.At(x, z + step), south = field.At(x, z - step);
            float dx = (east - west) / (2f * step), dz = (north - south) / (2f * step);
            float shade = Mathf.Clamp((dz - dx) * 0.45f, -0.25f, 0.18f);
            var ink = Color.Lerp(TurfInk.Hill, TurfInk.Highland, Mathf.InverseLerp(30f, 350f, height));
            ink *= 1f + shade; ink.a = 1f;
            ink = Color.Lerp(paper, ink, Mathf.InverseLerp(2f, 25f, height));
            float interval = height / 25f;
            float distance = Mathf.Abs(interval - Mathf.Round(interval)) * 25f;
            float width = Mathf.Min(2f, Mathf.Sqrt(dx * dx + dz * dz) * metres * 0.65f);
            return distance < width && height > 20f ? (Color32)Color.Lerp(ink, TurfInk.Contour, 0.4f) : (Color32)ink;
        }

        /// <summary>The region can fit even on a narrow window; no fixed world-size cap.</summary>
        public static float Ceiling(Rect region, float screenWidth, float screenHeight, float frame = 1.08f)
        {
            float aspect = Mathf.Max(0.2f, screenWidth / Mathf.Max(1f, screenHeight));
            return Mathf.Max(260f, Mathf.Max(region.height, region.width / aspect) * frame / DemoCamera.BoomToMetres);
        }
    }
}
