using System.Collections.Generic;
using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoverDemo
{
    // What the cover code sees, drawn on the street.
    //
    // The trouble with watching men take cover is that the interesting half of it is
    // invisible: a man ducks behind a bin and from a hundred metres up he is a man
    // stood near a bin. So this draws the three things that are actually being
    // decided - which boxes on this street count as cover at all, which flank each
    // man has picked, and whether he is down behind it or up over it shooting - and
    // counts, over the whole run, how much of the fighting was done from behind
    // something.
    //
    // It reads and draws only. Nothing here is asked by the fight; take the component
    // off and the same fight happens unwatched.
    public class CoverWatch : MonoBehaviour
    {
        // the same policy DemoCrews.CoverNear applies to a prop's box
        const float CoverMinHalf = 0.22f;
        const float CoverMaxHalf = 3f;
        const float BoxReach = 14f;      // how far round a fighting man boxes are drawn
        const float BoxEvery = 0.35f;    // and how often that survey is redone
        const float Width = 0.07f;       // the drawn line, in metres

        static readonly Color Boxes = new Color(0.35f, 0.85f, 0.95f, 1f);   // cover the code can offer
        static readonly Color Going = new Color(1f, 0.72f, 0.20f, 1f);      // on his way to a flank
        static readonly Color Up = new Color(0.40f, 0.95f, 0.45f, 1f);      // behind it, up, shooting
        static readonly Color Down = new Color(0.35f, 0.60f, 1f, 1f);       // behind it, ducked
        static readonly Color Open = new Color(1f, 0.35f, 0.30f, 1f);       // shooting from the open

        DemoCrews _crews;
        DemoCamera _cam;
        bool _on = true;

        Transform _root;
        readonly List<LineRenderer> _lines = new List<LineRenderer>();
        int _used;
        readonly Dictionary<Color, Material> _inks = new Dictionary<Color, Material>();

        readonly List<SidewalkPlan.Box> _near = new List<SidewalkPlan.Box>();
        readonly List<SidewalkPlan.Box> _cover = new List<SidewalkPlan.Box>();
        readonly HashSet<long> _seen = new HashSet<long>();
        float _survey;

        // the tally, kept over the whole run: seconds of fighting, and how many of them
        // were fought from behind something
        float _fightSeconds, _coverSeconds, _duckedSeconds;
        int _standing, _engaged, _inCover, _ducked;

        public void Init(DemoCrews crews, DemoCamera cam, bool on)
        {
            _crews = crews;
            _cam = cam;
            _on = on;
            _root = new GameObject("Cover Watch").transform;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) _on = !_on;

            Count(Time.deltaTime);

            _used = 0;
            if (_on && _crews != null)
            {
                DrawBoxes();
                DrawMen();
            }
            for (int i = _used; i < _lines.Count; i++)
                if (_lines[i].enabled) _lines[i].enabled = false;
        }

        // ------------------------------------------------------------------ counting

        void Count(float dt)
        {
            _standing = _engaged = _inCover = _ducked = 0;
            if (_crews == null) return;
            foreach (var unit in _crews.Units)
                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null) continue;
                    _standing++;
                    if (man.Target == null) continue;
                    _engaged++;
                    if (man.InCover) _inCover++;
                    if (man.Ducked) _ducked++;
                }
            // man-seconds, not runs: one man behind a bin for a whole fight and six men
            // behind six bins for a second each are not the same street, and the ratio
            // is the only number of this that survives a re-roll (the cycle draws from
            // the shared Random, so no two runs of one seed are the same run)
            _fightSeconds += _engaged * dt;
            _coverSeconds += _inCover * dt;
            _duckedSeconds += _ducked * dt;
        }

        // --------------------------------------------------------------- the drawing

        /// <summary>Every box within reach of a man in a fight that the cover query
        /// would accept - the street's furniture as the code sees it, which is not the
        /// same as the street's furniture: a palm is a trunk, a grate is paving, and a
        /// lamp post is too slim to hide anybody.</summary>
        void DrawBoxes()
        {
            _survey -= Time.deltaTime;
            if (_survey <= 0f)
            {
                _survey = BoxEvery;
                _cover.Clear();
                _seen.Clear();
                foreach (var unit in _crews.Units)
                    foreach (var man in unit.All())
                    {
                        if (man == null || man.Dead || man.Tf == null || man.Target == null) continue;
                        WalkObstacles.PropsNear(man.Tf.position, BoxReach, _near);
                        foreach (var b in _near)
                        {
                            if (b.Tall) continue;
                            if (Mathf.Min(b.H.x, b.H.y) < CoverMinHalf) continue;
                            if (Mathf.Max(b.H.x, b.H.y) > CoverMaxHalf) continue;
                            // one prop is reported to every man near it: keep it once,
                            // by where it stands to the nearest ten centimetres
                            long key = ((long)Mathf.RoundToInt(b.C.x * 10f) << 32) ^
                                       (uint)Mathf.RoundToInt(b.C.y * 10f);
                            if (_seen.Add(key)) _cover.Add(b);
                        }
                    }
            }

            foreach (var b in _cover)
            {
                var a = b.Ax * b.H.x;
                var c = b.Az * b.H.y;
                Vector3 P(Vector2 v) => new Vector3(b.C.x + v.x, 0.06f, b.C.y + v.y);
                Ring(Boxes, P(a + c), P(a - c), P(-a - c), P(-a + c));
            }
        }

        /// <summary>A line per man in a fight, from his chest to whatever the cover
        /// business has him doing: to the flank he is walking to, or to the man he is
        /// shooting at, in the colour of how he is stood.</summary>
        void DrawMen()
        {
            foreach (var unit in _crews.Units)
                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null) continue;
                    var from = man.Tf.position + Vector3.up * (man.Ducked ? 0.8f : 1.35f);

                    // the walk to cover is the half nobody sees: he leaves the fight,
                    // crosses open ground without firing, and arrives. Drawn to the spot
                    // itself so a flank chosen behind the wrong thing is obvious.
                    if (man.CoverSpot.HasValue && !man.InCover)
                    {
                        var spot = man.CoverSpot.Value;
                        spot.y = 0.1f;
                        Line(Going, from, spot);
                        Ring(Going,
                            spot + new Vector3(0.3f, 0f, 0.3f), spot + new Vector3(0.3f, 0f, -0.3f),
                            spot + new Vector3(-0.3f, 0f, -0.3f), spot + new Vector3(-0.3f, 0f, 0.3f));
                    }

                    if (man.Target == null || man.Target.Tf == null) continue;
                    var at = man.Target.Tf.position + Vector3.up * 1.2f;
                    Line(man.InCover ? (man.Ducked ? Down : Up) : Open, from, at);
                }
        }

        void Ring(Color ink, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var line = Take(ink, 5);
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.SetPosition(2, c);
            line.SetPosition(3, d);
            line.SetPosition(4, a);
        }

        void Line(Color ink, Vector3 a, Vector3 b)
        {
            var line = Take(ink, 2);
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }

        /// <summary>The next line out of the pool, dressed in this ink. Lines are kept
        /// and re-pointed rather than made and thrown away every frame - a fight is a
        /// hundred of them, sixty times a second.</summary>
        LineRenderer Take(Color ink, int points)
        {
            LineRenderer line;
            if (_used < _lines.Count) line = _lines[_used];
            else
            {
                var go = new GameObject("Line", typeof(LineRenderer));
                go.transform.SetParent(_root, false);
                line = go.GetComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.widthMultiplier = Width;
                line.numCapVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                _lines.Add(line);
            }
            _used++;
            line.enabled = true;
            line.positionCount = points;
            line.sharedMaterial = Ink(ink);
            return line;
        }

        /// <summary>One unlit material per colour. LineRenderer's start/end colours are
        /// vertex colours and URP's Unlit does not read them, so the colour has to be
        /// the material's - five of them, made once.</summary>
        Material Ink(Color colour)
        {
            if (_inks.TryGetValue(colour, out var mat)) return mat;
            // the ternary, never ??: a missing shader and a destroyed one are told apart
            // by Unity's own == and the coalescing operator does not ask it
            var urp = Shader.Find("Universal Render Pipeline/Unlit");
            var shader = urp != null ? urp : Shader.Find("Unlit/Color");
            mat = new Material(shader) { name = "Cover Watch " + colour };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", colour);
            _inks[colour] = mat;
            return mat;
        }

        void OnDestroy()
        {
            foreach (var mat in _inks.Values) if (mat) Destroy(mat);
            _inks.Clear();
        }

        // ------------------------------------------------------------------ the panel

        void OnGUI()
        {
            if (LivingCity.UI.PersonnelAlmanac.IsOpen) return;
            float top = Screen.height / 1080f * (_cam != null ? _cam.hintTopPx : 104f) + 30f;
            string line = _standing + " standing, " + _engaged + " in a fight, " + _inCover +
                " of them behind something (" + _ducked + " down)   -   over the run, " +
                Share(_coverSeconds) + " of the fighting was done from cover, " +
                Share(_duckedSeconds) + " of it ducked" + (_on ? "" : "   (lines off - C)");
            GUI.Label(new Rect(12f, top, 1400f, 24f), line);
            if (!_on) return;
            GUI.Label(new Rect(12f, top + 20f, 1400f, 24f),
                "boxes: what counts as cover   amber: walking to a flank   " +
                "green: behind it, shooting   blue: ducked   red: in the open");
        }

        string Share(float seconds) =>
            _fightSeconds < 0.5f ? "-" : (100f * seconds / _fightSeconds).ToString("0") + "%";
    }
}
