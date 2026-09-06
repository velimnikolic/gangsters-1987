using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// THE LINES BETWEEN THINGS ON A LEDGER MAP.
    ///
    /// THE LAW's sheet draws a case as a mind map, and a mind map is its links: a curve
    /// from the charge in the middle out to each man named and each witness, and a
    /// straight drop to counsel's read. uGUI has no line, and the two ways of faking one
    /// are both wrong here - a rotated <c>Image</c> per segment costs a GameObject and a
    /// draw call for every eighth of every curve, and a texture cannot follow geometry
    /// the page measures fresh at each paint.
    ///
    /// So one Graphic draws all of them into one mesh. It never takes a click: the
    /// nodes sit over it and the whole point of the layer is that it is under them.
    ///
    /// Points are given in STAGE PIXELS - x right, y DOWN from the stage's top-left,
    /// the frame the design and <see cref="PersonnelAlmanac"/>'s own map geometry are
    /// both written in. The conversion to the rect's own axes happens here, once.
    ///
    /// A GRAPHIC NEEDS ITS CANVAS RENDERER AND WILL NOT BE GIVEN ONE. The
    /// [RequireComponent] that <see cref="Graphic"/> carries is not applied when a
    /// SUBCLASS of it is added from script, so the component lands on a GameObject with
    /// no CanvasRenderer and the first time its page root is hidden
    /// MaskableGraphic.OnDisable dereferences the thing that is not there. The attribute
    /// below is the declaration; <see cref="PersonnelAlmanac"/> adds the renderer itself
    /// before adding this, because the attribute alone did not save it.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LedgerLinks : MaskableGraphic
    {
        /// <summary>How long a step of the walk is, in stage units. Four is smooth on a
        /// 400 unit sweep; the walk is never coarser than this and never finer than the
        /// cap below, whatever the curve's length.</summary>
        const float Step = 4f;

        const int LeastSteps = 12;
        const int MostSteps = 400;

        struct Link
        {
            public Vector2 A, ControlA, ControlB, B;
            public Color Ink;
            public float Width;

            /// <summary>Ink and air of a dashed line, in stage units; zero draws
            /// solid.</summary>
            public float Dash, Gap;
        }

        readonly List<Link> links = new List<Link>();

        /// <summary>Reused by the walk so a repaint of a five-link map allocates
        /// nothing after the first one.</summary>
        readonly List<Vector2> walk = new List<Vector2>(128);

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void Clear()
        {
            if (links.Count == 0)
                return;
            links.Clear();
            SetVerticesDirty();
        }

        /// <summary>A cubic between two points, with the control points the caller has
        /// already worked out - the map's ±52 unit reach off each end.</summary>
        public void AddCurve(Vector2 from, Vector2 controlFrom, Vector2 controlTo,
            Vector2 to, Color ink, float width, float dash = 0f, float gap = 0f)
        {
            links.Add(new Link
            {
                A = from,
                ControlA = controlFrom,
                ControlB = controlTo,
                B = to,
                Ink = ink,
                Width = Mathf.Max(0.5f, width),
                Dash = Mathf.Max(0f, dash),
                Gap = Mathf.Max(0f, gap),
            });
            SetVerticesDirty();
        }

        /// <summary>A straight run - the drop from the case to counsel's read. Written
        /// as a cubic whose controls lie on the line, so there is one walk and not
        /// two.</summary>
        public void AddLine(Vector2 from, Vector2 to, Color ink, float width,
            float dash = 0f, float gap = 0f) =>
            AddCurve(from, Vector2.Lerp(from, to, 1f / 3f),
                Vector2.Lerp(from, to, 2f / 3f), to, ink, width, dash, gap);

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (links.Count == 0)
                return;

            // The stage's own origin: PlaceTopLeft pivots every rect at its top-left, so
            // the rect's top-left corner is where a stage pixel of (0, 0) lands.
            var rect = rectTransform.rect;
            var origin = new Vector2(rect.xMin, rect.yMax);

            for (var i = 0; i < links.Count; i++)
                Draw(mesh, links[i], origin);
        }

        void Draw(VertexHelper mesh, Link link, Vector2 origin)
        {
            // How long the curve is, near enough to size the walk by: the control net
            // bounds it and the chord is under it, and the mean of the two is inside a
            // percent of the arc for a sweep as gentle as these.
            var net = (link.ControlA - link.A).magnitude +
                      (link.ControlB - link.ControlA).magnitude +
                      (link.B - link.ControlB).magnitude;
            var length = (net + (link.B - link.A).magnitude) * 0.5f;

            var steps = Mathf.Clamp(Mathf.CeilToInt(length / Step), LeastSteps,
                MostSteps);
            walk.Clear();
            for (var s = 0; s <= steps; s++)
                walk.Add(Point(link, s / (float)steps, origin));

            var half = link.Width * 0.5f;
            var dashed = link.Dash > 0f && link.Gap > 0f;
            var period = link.Dash + link.Gap;
            var travelled = 0f;

            for (var s = 0; s < steps; s++)
            {
                var a = walk[s];
                var b = walk[s + 1];
                var run = (b - a).magnitude;
                if (run <= Mathf.Epsilon)
                    continue;

                if (!dashed)
                {
                    // A solid run has its ends taken half a width past the joint, so a
                    // curve has no notch at any of its corners.
                    var along = (b - a) / run;
                    Quad(mesh, a - along * half, b + along * half, half, link.Ink);
                    travelled += run;
                    continue;
                }

                // A DASH IS CUT AT ITS OWN LENGTH, NOT THE WALK'S. Gating a whole step
                // on the phase at its middle makes the pattern the walk's resolution
                // rather than the 5-and-4 it was asked for, and the cadence then changes
                // with how far apart two nodes happen to stand. So each step is split
                // exactly where the pattern turns over.
                var cut = 0f;
                while (cut < run - Mathf.Epsilon)
                {
                    var phase = Mathf.Repeat(travelled + cut, period);
                    var inked = phase < link.Dash;
                    var until = inked ? link.Dash - phase : period - phase;
                    var span = Mathf.Min(until, run - cut);
                    if (inked && span > Mathf.Epsilon)
                        Quad(mesh, Vector2.LerpUnclamped(a, b, cut / run),
                            Vector2.LerpUnclamped(a, b, (cut + span) / run), half,
                            link.Ink);
                    cut += Mathf.Max(span, 0.01f);
                }
                travelled += run;
            }
        }

        static void Quad(VertexHelper mesh, Vector2 from, Vector2 to, float half,
            Color ink)
        {
            var run = to - from;
            var length = run.magnitude;
            if (length <= Mathf.Epsilon)
                return;
            var across = new Vector2(-run.y, run.x) / length * half;
            var first = mesh.currentVertCount;
            Vertex(mesh, from - across, ink);
            Vertex(mesh, from + across, ink);
            Vertex(mesh, to + across, ink);
            Vertex(mesh, to - across, ink);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first + 2, first + 3, first);
        }

        /// <summary>One point of the cubic, already in the rect's own axes - stage y
        /// runs DOWN, a rect's runs up.</summary>
        static Vector2 Point(Link link, float t, Vector2 origin)
        {
            var u = 1f - t;
            var stage = u * u * u * link.A
                        + 3f * u * u * t * link.ControlA
                        + 3f * u * t * t * link.ControlB
                        + t * t * t * link.B;
            return new Vector2(origin.x + stage.x, origin.y - stage.y);
        }

        static void Vertex(VertexHelper mesh, Vector2 at, Color ink)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = at;
            vertex.color = ink;
            vertex.uv0 = Vector2.zero;
            mesh.AddVert(vertex);
        }
    }
}
