using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What a crew is DOING, painted on the ground it is doing it on (UI-008).
    ///
    /// The approach mark says where a crew is going and the door beats say what it did
    /// when it got there, but a crew standing on a street it is working looked exactly
    /// like a crew standing on a street doing nothing - the only place the difference
    /// was written down was a line of words in the ledger's block file. This is that
    /// difference, top-down: a ring under the men with one word inside it.
    ///
    /// Paint, in the same idiom as <see cref="TurfMarks"/> - flat on the pavement, no
    /// collider, in the family's own colour, grown with the boom so the word survives
    /// being pulled back. It is drawn UNDER the men rather than over their heads for the
    /// same reason the door marks are: the game is played from above, and a marker in
    /// the sky belongs to a different game.
    ///
    /// It invents nothing about what a crew is at. The three things it can say are the
    /// three the simulation already keeps: a round being walked
    /// (<see cref="TerritoryRuntime.TryGetRound"/>), a doorstep errand on its way
    /// (<see cref="TerritoryRuntime.TryGetPendingApproach"/>), and men standing still on
    /// ground the outfit has a claim to. A crew that is merely crossing a street gets
    /// nothing, because it is not working it.
    /// </summary>
    public sealed class CrewWorkMark : MonoBehaviour
    {
        /// <summary>Seconds between sweeps. A crew's work changes at walking pace and a
        /// per-frame sweep of every unit would be the same picture for four times the
        /// cost - the same rate TurfMarks settled on.</summary>
        const float SweepEvery = 0.25f;

        const float RingOuter = 1.55f;
        const float RingInner = 1.30f;
        const int RingSegments = 40;
        const float Lift = 0.04f;
        const float TextLift = 0.06f;
        const float WordHeight = 0.55f;

        /// <summary>The boom the ring is drawn true size at, and the most it grows by -
        /// TurfMarks' own numbers, so two marks on one pavement grow together.</summary>
        const float TrueSizeHeight = 45f;
        const float MaxGrowth = 2.6f;

        /// <summary>A man is standing his ground rather than pausing mid-stride once he
        /// has been still this long. Without it a crew flickers a ring on and off every
        /// time it stops at a kerb.</summary>
        const float StandingFor = 2.5f;

        DemoCrews _crews;
        Transform _root;
        Camera _cam;
        float _next;

        static Mesh _ring;
        readonly Dictionary<Color, Material> _paints = new Dictionary<Color, Material>();

        struct Mark
        {
            public Transform Paint;
            public TextMeshPro Word;
            public string Said;
            public float Ground;
        }

        readonly Dictionary<int, Mark> _marks = new Dictionary<int, Mark>();
        readonly Dictionary<int, (Vector3 At, float Since)> _still =
            new Dictionary<int, (Vector3, float)>();
        readonly List<int> _gone = new List<int>();
        readonly HashSet<int> _working = new HashSet<int>();

        /// <summary>How many crews are marked as working right now - what a check holds
        /// against the number of crews that actually are.</summary>
        public int Painted => _marks.Count;

        public void Init(DemoCrews crews)
        {
            _crews = crews;
            _cam = Camera.main;
            _root = new GameObject("Crew Work Marks").transform;
            _root.SetParent(transform, false);
        }

        void Update()
        {
            if (_root == null || _crews == null)
                return;
            if (Time.time < _next)
                return;
            _next = Time.time + SweepEvery;

            Sweep();
            Grow();
        }

        void Sweep()
        {
            _working.Clear();
            for (var i = 0; i < _crews.Units.Count; i++)
            {
                var unit = _crews.Units[i];
                // Ours only. What another house has its men doing is something the
                // player learns by watching them do it, not off a label.
                if (unit == null || unit.Faction != 0 || unit.Wiped)
                    continue;

                var word = WorkOf(unit);
                if (word.Length == 0)
                    continue;

                _working.Add(unit.CrewId);
                Lay(unit, word);
            }

            _gone.Clear();
            foreach (var pair in _marks)
                if (!_working.Contains(pair.Key))
                    _gone.Add(pair.Key);
            for (var i = 0; i < _gone.Count; i++)
            {
                if (_marks.TryGetValue(_gone[i], out var mark) && mark.Paint != null)
                    Destroy(mark.Paint.gameObject);
                _marks.Remove(_gone[i]);
                _still.Remove(_gone[i]);
            }
        }

        /// <summary>
        /// What this crew is at, in one word, or nothing where it is not working. Read
        /// off the simulation's own state in the order that matters: money being carried
        /// beats an errand, an errand beats standing about.
        /// </summary>
        string WorkOf(DemoCrews.Unit unit)
        {
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null)
                return "";

            if (runtime.TryGetRound(unit.CrewId, out var carried, out var stopsLeft, out _))
                return stopsLeft > 0
                    ? "COLLECTING · " + stopsLeft + " LEFT"
                    : carried > 0
                        ? "CARRYING THE TAKE"
                        : "COLLECTING";

            if (runtime.TryGetPendingApproach(unit.CrewId, out _))
                return "ON AN ERRAND";

            return Standing(unit) ? "STANDING IT" : "";
        }

        /// <summary>
        /// Are these men holding this ground, or merely on it? They have to have been
        /// still for a beat, and the ground has to be ground the outfit has some claim
        /// to - men standing on a street nobody has ever worked are men waiting, not men
        /// working, and saying otherwise would be the label writing its own fact.
        /// </summary>
        bool Standing(DemoCrews.Unit unit)
        {
            var at = unit.Position;
            if (_still.TryGetValue(unit.CrewId, out var was) &&
                (was.At - at).sqrMagnitude < 2.5f * 2.5f)
            {
                if (Time.time - was.Since < StandingFor)
                    return false;
            }
            else
            {
                _still[unit.CrewId] = (at, Time.time);
                return false;
            }

            var runtime = TerritoryRuntime.Instance;
            if (runtime?.Control == null ||
                !runtime.TryGetBlockAtWorld(at, out var blockId))
                return false;

            var state = runtime.Control.StateOf(blockId);
            if (state != LivingCity.Territory.TerritoryControlState.Influenced &&
                state != LivingCity.Territory.TerritoryControlState.Contested &&
                state != LivingCity.Territory.TerritoryControlState.Controlled &&
                state != LivingCity.Territory.TerritoryControlState.Dominated)
                return false;

            var leader = runtime.Control.LeaderOf(blockId);
            return !leader.IsValid ||
                   leader.Value == LivingCity.Gangs.GangCatalog.PlayerGangId;
        }

        void Lay(DemoCrews.Unit unit, string word)
        {
            var at = unit.Position;
            var ground = _crews != null ? _crews.GroundY : 0f;
            var tint = LivingCity.UI.GangPalette.Of(LivingCity.Gangs.GangCatalog.PlayerGangId);

            if (!_marks.TryGetValue(unit.CrewId, out var mark) || mark.Paint == null)
            {
                var paint = new GameObject("Work · crew " + unit.CrewId).transform;
                paint.SetParent(_root, false);

                var ring = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
                ring.transform.SetParent(paint, false);
                ring.GetComponent<MeshFilter>().sharedMesh = Ring();
                var renderer = ring.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = Ink(tint);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var label = new GameObject("Word").AddComponent<TextMeshPro>();
                label.transform.SetParent(paint, false);
                label.transform.localPosition =
                    new Vector3(0f, 0f, -(TextLift - Lift));
                label.font = LivingCity.UI.LedgerStyle.Condensed;
                label.color = tint;
                label.fontSize = WordHeight * 10f;
                label.enableAutoSizing = false;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                label.characterSpacing = 6f;
                label.rectTransform.sizeDelta = new Vector2(8f, WordHeight * 1.6f);
                var wordRenderer = label.GetComponent<MeshRenderer>();
                wordRenderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                wordRenderer.receiveShadows = false;

                mark = new Mark { Paint = paint, Word = label, Said = "", Ground = ground };
            }

            // Flat on the pavement, north up the page. Forward points INTO the ground,
            // because the visible face of a flat mesh is -forward: aimed at the sky, the
            // word would be read from its back.
            mark.Paint.SetPositionAndRotation(
                new Vector3(at.x, ground + Lift, at.z),
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            mark.Ground = ground;

            if (!string.Equals(mark.Said, word, System.StringComparison.Ordinal))
            {
                mark.Word.text = word;
                mark.Said = word;
            }

            _marks[unit.CrewId] = mark;
        }

        /// <summary>World-sized paint vanishes when the boom pulls back, so it grows
        /// with the boom - the same readability rule TurfMarks and the turf plate's own
        /// lettering are given.</summary>
        void Grow()
        {
            if (_cam == null)
                _cam = Camera.main;
            if (_cam == null)
                return;

            foreach (var pair in _marks)
            {
                var mark = pair.Value;
                if (mark.Paint == null)
                    continue;
                var boom = Mathf.Max(1f, _cam.transform.position.y - mark.Ground);
                mark.Paint.localScale =
                    Vector3.one * Mathf.Clamp(boom / TrueSizeHeight, 1f, MaxGrowth);
            }
        }

        /// <summary>The ring, once for the whole city. Wound both ways, like the door
        /// arrows: which face of a flat mesh is the front depends on the frame it is
        /// dropped into, and a mark invisible from above is no mark at all.</summary>
        static Mesh Ring()
        {
            if (_ring != null)
                return _ring;

            var points = new List<Vector3>(RingSegments * 2);
            for (var i = 0; i < RingSegments; i++)
            {
                var a = i / (float)RingSegments * Mathf.PI * 2f;
                var cos = Mathf.Cos(a);
                var sin = Mathf.Sin(a);
                points.Add(new Vector3(cos * RingOuter, sin * RingOuter, 0f));
                points.Add(new Vector3(cos * RingInner, sin * RingInner, 0f));
            }

            var faces = new List<int>(RingSegments * 12);
            for (var i = 0; i < RingSegments; i++)
            {
                var o0 = i * 2;
                var i0 = o0 + 1;
                var o1 = (i + 1) % RingSegments * 2;
                var i1 = o1 + 1;
                faces.Add(o0); faces.Add(o1); faces.Add(i0);
                faces.Add(i0); faces.Add(o1); faces.Add(i1);
                faces.Add(o0); faces.Add(i0); faces.Add(o1);
                faces.Add(i0); faces.Add(i1); faces.Add(o1);
            }

            _ring = new Mesh { name = "Crew Work Ring" };
            _ring.SetVertices(points);
            _ring.SetTriangles(faces, 0);
            _ring.RecalculateNormals();
            _ring.RecalculateBounds();
            return _ring;
        }

        Material Ink(Color colour)
        {
            if (_paints.TryGetValue(colour, out var paint))
                return paint;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color");
            paint = new Material(shader) { name = "Crew Work Mark " + colour };
            if (paint.HasProperty("_BaseColor")) paint.SetColor("_BaseColor", colour);
            if (paint.HasProperty("_Color")) paint.SetColor("_Color", colour);
            _paints[colour] = paint;
            return paint;
        }

        void OnDestroy()
        {
            foreach (var paint in _paints.Values)
                if (paint != null)
                    Destroy(paint);
            _paints.Clear();
        }
    }
}
