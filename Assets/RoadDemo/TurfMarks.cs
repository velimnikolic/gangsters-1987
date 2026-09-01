using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The word painted on the pavement outside a place a family holds: an arrow pointing
    /// at the door, what the place IS under it, and the house's name under that - all of
    /// it in the family's own colour, so the city can be read from the boom without
    /// clicking a single building.
    ///
    /// Paint, not furniture. No collider, no claim on the pavement, nothing for a walker
    /// to go round: men walk over it exactly as they walk over a road line. It is laid
    /// flat on the ground rather than floated over the roof because the game is played
    /// from above, and because a marker in the sky belongs to a different game.
    ///
    /// Ours is painted from the first day. A RIVAL'S is not: their door is a rumour until
    /// a crew of ours has been down that street (<see cref="TurfKnowledge"/>), and the
    /// mark goes up the moment one has. The mark grows with the boom so the word stays
    /// legible pulled back, the way the turf map's own lettering does - the city is meant
    /// to be readable at every height, not only at head height.
    ///
    /// The word is the front's <see cref="GangFront.Role"/>, which is why it is a string:
    /// every later premises an outfit takes paints its own word here and needs no new
    /// code to do it.
    /// </summary>
    public sealed class TurfMarks : MonoBehaviour
    {
        /// <summary>Seconds between sweeps. The marks do not move and knowledge changes
        /// at walking pace; a per-frame sweep of every front would be four times the work
        /// for the same picture.</summary>
        const float SweepEvery = 0.25f;

        // The mark's own flat frame, in metres: +Y points AT the building, so the arrow
        // runs up the page and the words sit below it - a man coming down the street
        // reads them the right way up, the way a word painted on a road is read.
        const float ArrowTip = 1.35f;
        const float HeadBase = 0.60f;
        const float HeadHalf = 0.62f;
        const float ShaftHalf = 0.20f;
        const float ShaftEnd = 0.10f;
        const float RoleMid = -0.48f;
        const float RoleHeight = 0.86f;
        const float HouseMid = -1.16f;
        const float HouseHeight = 0.34f;

        /// <summary>Off the ground, so paint does not fight the pavement for the same
        /// depth - and the lettering a hair above the arrow, for the same reason.</summary>
        const float Lift = 0.035f;
        const float TextLift = 0.055f;

        /// <summary>The boom height the mark is drawn at its true size, and the most it
        /// will ever be grown by. Past this the tactical map takes over anyway.</summary>
        const float TrueSizeHeight = 45f;
        const float MaxGrowth = 2.6f;

        DemoCrews _crews;
        Transform _root;
        Camera _cam;
        float _next;

        static Mesh _arrow;
        readonly Dictionary<Color, Material> _paints = new Dictionary<Color, Material>();

        /// <summary>One painted mark, kept beside the front it belongs to. Keyed by the
        /// front's entity id rather than by the component: a destroyed UnityEngine.Object
        /// compares equal to null AND to every other destroyed object, which is exactly
        /// the wrong key for a dictionary that outlives a scene change.</summary>
        struct Mark
        {
            public GangFront Front;
            public Transform Paint;
        }

        readonly Dictionary<EntityId, Mark> _marks = new Dictionary<EntityId, Mark>();
        readonly List<EntityId> _gone = new List<EntityId>();

        /// <summary>How many marks are painted right now - what the audit holds against
        /// the number of places the outfit is supposed to be able to see.</summary>
        public int Painted => _marks.Count;

        public void Init(DemoCrews crews)
        {
            _crews = crews;
            _cam = Camera.main;
            _root = new GameObject("Turf Marks").transform;
            _root.SetParent(transform, false);
        }

        void Update()
        {
            if (_root == null) return;
            if (Time.time < _next) return;
            _next = Time.time + SweepEvery;

            Discover();
            Sweep();
            Grow();
        }

        // ----------------------------------------------------------------- learning

        /// <summary>A rival's door becomes known when one of OUR crews has been within a
        /// street's width of it. Nothing else reveals a place: not the camera, not the
        /// ledger, not owning the block it stands on.</summary>
        void Discover()
        {
            if (_crews == null) return;

            var fronts = GangFront.All;
            for (int i = 0; i < fronts.Count; i++)
            {
                var front = fronts[i];
                if (front == null || TurfKnowledge.IsKnown(front))
                    continue;

                var door = front.Outside;
                for (int u = 0; u < _crews.Units.Count; u++)
                {
                    var unit = _crews.Units[u];
                    if (unit == null || unit.Faction != 0 || unit.Wiped)
                        continue;

                    var at = unit.Position;
                    float dx = at.x - door.x, dz = at.z - door.z;
                    if (dx * dx + dz * dz > TurfKnowledge.LearnRange * TurfKnowledge.LearnRange)
                        continue;

                    if (TurfKnowledge.Learn(front))
                        Debug.Log($"[Turf] {front.GangName}'s {front.Role} found - " +
                                  (front.Books != null ? front.Books.Sign : front.name) +
                                  (front.Books != null && front.Books.Address.Length > 0
                                      ? ", " + front.Books.Address : ""));
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ painting

        void Sweep()
        {
            var fronts = GangFront.All;
            for (int i = 0; i < fronts.Count; i++)
            {
                var front = fronts[i];
                if (front == null || !TurfKnowledge.IsKnown(front))
                    continue;
                var key = front.GetEntityId();
                if (!_marks.ContainsKey(key))
                    _marks[key] = new Mark { Front = front, Paint = Lay(front) };
            }

            // A front whose building was streamed out takes its mark with it.
            _gone.Clear();
            foreach (var pair in _marks)
                if (pair.Value.Front == null)
                    _gone.Add(pair.Key);
            for (int i = 0; i < _gone.Count; i++)
            {
                if (_marks.TryGetValue(_gone[i], out var mark) && mark.Paint != null)
                    Destroy(mark.Paint.gameObject);
                _marks.Remove(_gone[i]);
            }
        }

        /// <summary>Lay one mark on the pavement outside a door.</summary>
        Transform Lay(GangFront front)
        {
            var tint = LivingCity.UI.GangPalette.Of(front.GangId);
            var at = front.Outside;

            // Which way the facade faces. A front with no measured normal (a demo scene
            // that bound one by hand) falls back to the line from the doorstep out to
            // the pavement, which is the same direction by construction.
            var outward = front.Outward;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f)
            {
                outward = at - front.Door;
                outward.y = 0f;
            }
            outward = outward.sqrMagnitude < 1e-4f ? Vector3.forward : outward.normalized;

            var mark = new GameObject("Mark · " + front.GangName + " · " + front.Role)
                .transform;
            mark.SetParent(_root, false);
            // Flat on the ground, facing the sky, with the letters' tops toward the
            // building: read by a man walking up to the door, not by one inside it.
            mark.SetPositionAndRotation(
                new Vector3(at.x, at.y + Lift, at.z),
                Quaternion.LookRotation(Vector3.up, -outward));

            var arrow = new GameObject("Arrow", typeof(MeshFilter), typeof(MeshRenderer));
            arrow.transform.SetParent(mark, false);
            arrow.GetComponent<MeshFilter>().sharedMesh = Arrow();
            var renderer = arrow.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Ink(tint);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Word(mark, front.Role, tint, RoleMid, RoleHeight, 1f);
            Word(mark, front.GangName, tint, HouseMid, HouseHeight, 0.78f);
            return mark;
        }

        /// <summary>One line of the mark's lettering, laid in the same flat frame as the
        /// arrow. Condensed and letterspaced - the city's own stencil, the face the turf
        /// map letters its streets in.</summary>
        void Word(Transform mark, string words, Color tint, float mid, float height,
                  float alpha)
        {
            if (string.IsNullOrEmpty(words)) return;

            var go = new GameObject("Word " + words);
            go.transform.SetParent(mark, false);
            go.transform.localPosition = new Vector3(0f, mid, -(TextLift - Lift));

            var text = go.AddComponent<TextMeshPro>();
            text.font = LivingCity.UI.LedgerStyle.Condensed;
            text.text = words.ToUpperInvariant();
            text.color = new Color(tint.r, tint.g, tint.b, alpha);
            // TMP's 3D lettering is measured in tenths of a world unit.
            text.fontSize = height * 10f;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.characterSpacing = 8f;
            text.rectTransform.sizeDelta = new Vector2(8f, height * 1.6f);

            var renderer = text.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>The lettering is world-sized, so pulled back it would vanish. It
        /// grows with the boom instead - the same deliberate readability the turf map's
        /// own labels are given, and the reason "always visible" means anything.</summary>
        void Grow()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            foreach (var pair in _marks)
            {
                var paint = pair.Value.Paint;
                if (paint == null) continue;
                var boom = Mathf.Max(1f, _cam.transform.position.y - paint.position.y);
                paint.localScale = Vector3.one *
                    Mathf.Clamp(boom / TrueSizeHeight, 1f, MaxGrowth);
            }
        }

        // ------------------------------------------------------------------- the kit

        /// <summary>The arrow, once for the whole city - every mark is the same shape in
        /// its own frame and only the paint differs. Wound both ways: which face of a
        /// flat mesh is the front depends on the frame it is dropped into, and a mark
        /// that is invisible from above is no mark at all.</summary>
        static Mesh Arrow()
        {
            if (_arrow != null) return _arrow;

            var points = new[]
            {
                new Vector3(0f, ArrowTip, 0f),
                new Vector3(-HeadHalf, HeadBase, 0f),
                new Vector3(HeadHalf, HeadBase, 0f),
                new Vector3(-ShaftHalf, HeadBase, 0f),
                new Vector3(ShaftHalf, HeadBase, 0f),
                new Vector3(-ShaftHalf, ShaftEnd, 0f),
                new Vector3(ShaftHalf, ShaftEnd, 0f),
            };

            var faces = new[] { 0, 2, 1, 3, 4, 6, 3, 6, 5 };
            var both = new int[faces.Length * 2];
            for (int i = 0; i < faces.Length; i += 3)
            {
                both[i] = faces[i];
                both[i + 1] = faces[i + 1];
                both[i + 2] = faces[i + 2];
                both[faces.Length + i] = faces[i];
                both[faces.Length + i + 1] = faces[i + 2];
                both[faces.Length + i + 2] = faces[i + 1];
            }

            _arrow = new Mesh { name = "Turf Mark Arrow" };
            _arrow.SetVertices(new List<Vector3>(points));
            _arrow.SetTriangles(both, 0);
            _arrow.RecalculateNormals();
            _arrow.RecalculateBounds();
            return _arrow;
        }

        Material Ink(Color colour)
        {
            if (_paints.TryGetValue(colour, out var paint)) return paint;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color");
            paint = new Material(shader) { name = "Turf Mark " + colour };
            if (paint.HasProperty("_BaseColor")) paint.SetColor("_BaseColor", colour);
            if (paint.HasProperty("_Color")) paint.SetColor("_Color", colour);
            _paints[colour] = paint;
            return paint;
        }

        void OnDestroy()
        {
            foreach (var paint in _paints.Values)
                if (paint != null) Destroy(paint);
            _paints.Clear();
            if (_root != null) Destroy(_root.gameObject);
        }
    }
}
