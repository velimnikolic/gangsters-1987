using UnityEngine;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// Keeps a RawImage's picture in its own proportions. A RawImage stretches its
    /// texture across whatever rectangle it is given, so a square studio print
    /// (<see cref="PortraitStudio"/> shoots 256x256) hung in a tall card grew the man
    /// in it, and the same print in a wide press slot squashed him. This component
    /// crops instead: it shows the largest centred region of the texture that already
    /// has the plate's proportions, so a face is never widened or stretched - only
    /// framed a little tighter on one axis.
    ///
    /// Cropping and not letterboxing, because every plate in the book has a printed
    /// ground under it (the initials, the hatched press plate): bars of that ground
    /// down the sides of a photograph would read as a mistake, where a tighter crop
    /// reads as the crop it is. The subject is centred in every framing the studio
    /// shoots, so the tighter crop takes background, not head.
    ///
    /// It re-fits whenever the rectangle changes, so a page that re-lays out (the
    /// catalogue's columns follow the page width) keeps the proportions it was given.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class PortraitFit : MonoBehaviour
    {
        RawImage image;
        RectTransform frame;

        /// <summary>
        /// Put a fit on this image and apply it now. Safe to call on every print: the
        /// component is added once and answers every later print through the same
        /// RawImage. GetComponent is tested with an explicit branch and not "??" - the
        /// null-coalescing operator does not see Unity's fake null and would skip the
        /// AddComponent (see the same trap in OverlayRegistry).
        /// </summary>
        public static void Attach(RawImage target)
        {
            if (!target)
                return;

            var fit = target.GetComponent<PortraitFit>();
            if (!fit)
                fit = target.gameObject.AddComponent<PortraitFit>();
            fit.Fit();
        }

        void OnEnable() => Fit();

        // The plate is placed by hand and may be sized after the print lands.
        void OnRectTransformDimensionsChange() => Fit();

        /// <summary>
        /// Centre-crop the texture to the plate's proportions. Whichever axis has room
        /// to spare is the one that gets trimmed; the other stays whole.
        /// </summary>
        public void Fit()
        {
            if (!image)
                image = GetComponent<RawImage>();
            if (!frame)
                frame = transform as RectTransform;
            if (!image || !frame)
                return;

            var texture = image.texture;
            if (!texture || texture.width <= 0 || texture.height <= 0)
                return;

            var size = frame.rect.size;
            if (size.x <= 0f || size.y <= 0f)
                return;

            var plate = size.x / size.y;
            var print = texture.width / (float)texture.height;

            if (plate > print)
            {
                // Wider than the print: keep the full width, show a middle band.
                var band = print / plate;
                image.uvRect = new Rect(0f, (1f - band) * 0.5f, 1f, band);
            }
            else
            {
                // Taller than the print: keep the full height, show a middle column.
                var band = plate / print;
                image.uvRect = new Rect((1f - band) * 0.5f, 0f, band, 1f);
            }
        }
    }
}
