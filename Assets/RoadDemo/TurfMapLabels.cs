using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>One street name, where it goes and how big. The box is what the
    /// crossings pass steers around.</summary>
    public sealed class TurfLabel
    {
        public string Text = "";

        /// <summary>Authored-space centre of the lettering.</summary>
        public Vector2 Plan;

        /// <summary>Read up the sheet rather than across it - a name on a north-south
        /// street.</summary>
        public bool Vertical;

        /// <summary>Raster pixels of type. Eleven for a street, twelve for a
        /// boulevard: the floor a survey plate's lettering can be set at and still be
        /// read, and the design's own numbers.</summary>
        public float Size = 11f;

        /// <summary>The box in authored units, oriented the way the name is actually
        /// printed. Worked out during the draw from the width the face measured at
        /// prepare.</summary>
        public Rect Box;
    }

    /// <summary>
    /// The lettering rig: real Oswald, floating over the plate rather than printed
    /// into it.
    ///
    /// This used to be a camera, a render texture and a ReadPixels: the names were set
    /// on a private canvas, photographed, and composited into the ground plate a pixel
    /// at a time. It looked right at one zoom and only one. A name stamped into the
    /// paper is paper - held closer it magnifies with everything else, and the design
    /// is explicit that street names must NOT be baked for exactly that reason. It also
    /// cost a synchronous GPU read-back and two megabytes of garbage on every survey,
    /// on a map whose whole problem was surveys costing too much.
    ///
    /// So the names are TextMeshPro, parented to the sheet itself. The sheet is scaled
    /// to fit the boom; the letters ride that scale, which keeps them the size the
    /// design sets them in PLATE pixels at every zoom while staying vector-sharp on any
    /// window. Nothing here runs per frame - the pool is refilled once per published
    /// survey and then simply hangs on the paper.
    ///
    /// The rig is also the RULER. The crossings pass refuses to lay a zebra across a
    /// name, so the survey needs every name's width before it draws - and it draws on a
    /// worker thread where TextMeshPro cannot be asked anything. <see cref="Measure"/>
    /// is called once, at prepare, and the widths are banked.
    ///
    /// All type goes through LedgerStyle, the project's single source for faces.
    /// </summary>
    public sealed class TurfMapLabels : MonoBehaviour
    {
        /// <summary>TMP's character spacing is a percentage of the point size. The
        /// design letterspaces the names by a pixel and a half at eleven, which is
        /// this.</summary>
        const float Tracking = 13f;

        RectTransform _root;
        readonly List<TextMeshProUGUI> _pool = new List<TextMeshProUGUI>();
        float _labelScale = 1f;

        /// <summary>Scale the live lettering down as the full turf map is pulled back.
        /// The map plate is redrawn per view, so without this deliberate readability
        /// falloff the camera zoom cancels out and every street name stays the same size
        /// on screen.</summary>
        public void SetZoomOut(float amount)
        {
            amount = Mathf.Clamp01(amount);
            _labelScale = Mathf.Lerp(1f, 0.18f, amount);
            for (int i = 0; i < _pool.Count; i++)
            {
                var label = _pool[i];
                if (label != null)
                    label.rectTransform.localScale = Vector3.one * _labelScale;
            }
        }

        /// <summary>The one text used for measuring, kept off the sheet.</summary>
        TextMeshProUGUI _ruler;

        /// <summary>
        /// Hangs the rig on the sheet. Everything the rig sets - a position in plate
        /// pixels, a point size in plate pixels - is in the sheet's own coordinates, so
        /// the sheet's scale carries the lettering with the paper and no label has to be
        /// told the boom has moved.
        /// </summary>
        public void Attach(RectTransform sheet)
        {
            _root = DemoUi.NewRect("Street Names", sheet);
            DemoUi.Fill(_root);

            _ruler = NewText("Ruler");
            _ruler.gameObject.SetActive(false);
        }

        TextMeshProUGUI NewText(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = LivingCity.UI.LedgerStyle.Condensed;
            text.color = TurfInk.Street;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.characterSpacing = Tracking;
            text.raycastTarget = false;

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(TurfPlate.RW, 32f);
            return text;
        }

        // ------------------------------------------------------------- measuring

        /// <summary>
        /// What the face sets for one string at one point size. Drawn from TMP's own
        /// metrics - guessing a width from the character count is how a zebra ends up
        /// half under a street name.
        /// </summary>
        public float Measure(string text, float size)
        {
            if (_ruler == null || string.IsNullOrEmpty(text))
                return 0f;

            _ruler.fontSize = size;
            _ruler.text = text;
            return _ruler.GetPreferredValues(text).x;
        }

        // -------------------------------------------------------------- setting

        /// <summary>
        /// Sets the names of the survey that has just been published. Called once per
        /// plate and not per frame: the letters are children of the sheet, so a pan or a
        /// zoom moves them with the paper for free.
        /// </summary>
        public void Set(IList<TurfLabel> labels)
        {
            if (_root == null)
                return;

            while (_pool.Count < labels.Count)
                _pool.Add(NewText("Name " + _pool.Count));

            for (int i = 0; i < _pool.Count; i++)
            {
                bool used = i < labels.Count;
                _pool[i].gameObject.SetActive(used);
                if (!used)
                    continue;

                var label = labels[i];
                var text = _pool[i];
                text.fontSize = label.Size;
                text.text = label.Text;

                var rect = (RectTransform)text.transform;
                rect.anchoredPosition = new Vector2(
                    label.Plan.x * TurfPlate.S - TurfPlate.RW * 0.5f,
                    label.Plan.y * TurfPlate.S - TurfPlate.RH * 0.5f);
                rect.localRotation = Quaternion.Euler(0f, 0f, label.Vertical ? 90f : 0f);
                rect.localScale = Vector3.one * _labelScale;
            }
        }

        /// <summary>Takes every name off the sheet - the map going down, so an old
        /// view's lettering is not what the next one opens on.</summary>
        public void Clear()
        {
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i] != null)
                    _pool[i].gameObject.SetActive(false);
        }
    }
}
