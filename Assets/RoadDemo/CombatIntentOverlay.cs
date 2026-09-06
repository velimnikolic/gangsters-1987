using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>Draws the combat decisions that are otherwise invisible: usable cover,
    /// the cover spot a man is moving to, and the enemy he is aiming at. DemoCrews owns
    /// one of these in every scene, so the same I-key view follows the combat system
    /// instead of belonging to a particular demo.</summary>
    public sealed class CombatIntentOverlay : MonoBehaviour
    {
        // the same policy DemoCrews.CoverNear applies to a prop's box
        const float BoxReach = 14f;
        const float BoxEvery = 0.35f;
        const float Width = 0.07f;

        static readonly Color Boxes = new Color(0.35f, 0.85f, 0.95f, 1f);
        static readonly Color Going = new Color(1f, 0.72f, 0.20f, 1f);
        static readonly Color Up = new Color(0.40f, 0.95f, 0.45f, 1f);
        static readonly Color Down = new Color(0.35f, 0.60f, 1f, 1f);
        static readonly Color Open = new Color(1f, 0.35f, 0.30f, 1f);
        // lying in wait: a flank held with nobody to shoot at yet (EPIC 28)
        static readonly Color Waiting = new Color(0.85f, 0.80f, 0.35f, 1f);
        static readonly Color Driving = new Color(0.92f, 0.48f, 1f, 1f);
        static readonly Color Walking = new Color(0.25f, 0.95f, 0.82f, 1f);
        static readonly Color Running = new Color(1f, 0.82f, 0.20f, 1f);

        DemoCrews _crews;
        bool _visible;

        Transform _root;
        readonly List<LineRenderer> _lines = new List<LineRenderer>();
        readonly List<Vector3[]> _pathPoints = new List<Vector3[]>();
        readonly List<int> _pathCounts = new List<int>();
        readonly Dictionary<Color, Material> _inks = new Dictionary<Color, Material>();
        int _used;

        readonly List<SidewalkPlan.Box> _near = new List<SidewalkPlan.Box>();
        readonly List<SidewalkPlan.Box> _cover = new List<SidewalkPlan.Box>();
        readonly HashSet<long> _seen = new HashSet<long>();
        readonly List<Vector3> _carPath = new List<Vector3>();
        readonly List<Vector3> _walkPath = new List<Vector3>();
        readonly List<Vector3> _trunkPath = new List<Vector3>();
        readonly List<CrewWalker> _movingMen = new List<CrewWalker>();
        float _survey;

        public bool IsVisible => _visible;

        public void Init(DemoCrews crews, bool visible = true)
        {
            _crews = crews;
            if (_root == null)
            {
                _root = new GameObject("Combat Intent Overlay").transform;
                _root.SetParent(transform, false);
            }
            SetVisible(visible);
        }

        /// <summary>Lets a specialised bench choose its initial state without owning
        /// the overlay. Runtime interaction still goes through the common I shortcut.</summary>
        public void SetVisible(bool visible, bool announce = false)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (_visible) _survey = 0f;
            else HideLines();

            if (announce)
                CrewOverlay.Announce("INDICATORS ARE " + (_visible ? "ON" : "OFF"),
                    2f, _visible ? Going : Color.white);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.iKey.wasPressedThisFrame &&
                !LivingCity.UI.PersonnelAlmanac.IsOpen)
                SetVisible(!_visible, announce: true);

            _used = 0;
            if (_visible && _crews != null)
            {
                DrawBoxes();
                DrawMen();
                DrawCars();
            }
            HideUnusedLines();
        }

        void HideLines()
        {
            _used = 0;
            HideUnusedLines();
        }

        void HideUnusedLines()
        {
            for (int i = _used; i < _lines.Count; i++)
                if (_lines[i].enabled) _lines[i].enabled = false;
        }

        /// <summary>Every nearby prop box that the shared cover query would accept.</summary>
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
                        foreach (var box in _near)
                        {
                            if (box.Tall) continue;
                            if (Mathf.Min(box.H.x, box.H.y) < DemoCrews.PropCoverMinHalf) continue;
                            if (Mathf.Max(box.H.x, box.H.y) > DemoCrews.PropCoverMaxHalf) continue;

                            // The same prop is reported once for every nearby fighter.
                            long key = ((long)Mathf.RoundToInt(box.C.x * 10f) << 32) ^
                                       (uint)Mathf.RoundToInt(box.C.y * 10f);
                            if (_seen.Add(key)) _cover.Add(box);
                        }
                    }
            }

            foreach (var box in _cover)
            {
                var along = box.Ax * box.H.x;
                var across = box.Az * box.H.y;
                Vector3 Point(Vector2 offset) =>
                    new Vector3(box.C.x + offset.x, 0.06f, box.C.y + offset.y);
                Ring(Boxes, Point(along + across), Point(along - across),
                    Point(-along - across), Point(-along + across));
            }
        }

        /// <summary>A line from each fighter to his chosen cover spot and current target.</summary>
        void DrawMen()
        {
            foreach (var unit in _crews.Units)
            {
                if (unit.Faction == 0) DrawWalk(unit);
                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null) continue;
                    var from = man.Tf.position + Vector3.up * (man.Ducked ? 0.8f : 1.35f);

                    // A FLANK HE IS HOLDING, with no fight on it: the ambush. Drawn
                    // before the fighting colours because a waiting man has no target
                    // line at all and would otherwise be the one man on the street the
                    // indicators say nothing about.
                    if (man.HeldCover.HasValue && man.Target == null)
                    {
                        var held = man.HeldCover.Value;
                        held.y = 0.1f;
                        if (!man.Lurking) Line(Waiting, from, held);
                        Ring(Waiting,
                            held + new Vector3(0.35f, 0f, 0.35f),
                            held + new Vector3(0.35f, 0f, -0.35f),
                            held + new Vector3(-0.35f, 0f, -0.35f),
                            held + new Vector3(-0.35f, 0f, 0.35f));
                        continue;
                    }

                    if (man.CoverSpot.HasValue && !man.InCover)
                    {
                        var spot = man.CoverSpot.Value;
                        spot.y = 0.1f;
                        Line(Going, from, spot);
                        Ring(Going,
                            spot + new Vector3(0.3f, 0f, 0.3f),
                            spot + new Vector3(0.3f, 0f, -0.3f),
                            spot + new Vector3(-0.3f, 0f, -0.3f),
                            spot + new Vector3(-0.3f, 0f, 0.3f));
                    }

                    if (man.Target == null || man.Target.Tf == null) continue;
                    var target = man.Target.Tf.position + Vector3.up * 1.2f;
                    Line(man.InCover ? (man.Ducked ? Down : Up) : Open, from, target);
                }
            }
        }

        /// <summary>A crew moves as a formation, so its intent reads as one route:
        /// one branch from every man joins the leader's way, then the way fans back
        /// out to every man's exact formation spot.</summary>
        void DrawWalk(DemoCrews.Unit unit)
        {
            _movingMen.Clear();
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Tf != null && !man.Riding &&
                    (man.State == CrewWalker.Mode.Walking ||
                     man.State == CrewWalker.Mode.Homing ||
                     man.State == CrewWalker.Mode.Striding))
                    _movingMen.Add(man);
            if (_movingMen.Count == 0) return;

            var lead = unit.Boss != null && _movingMen.Contains(unit.Boss)
                ? unit.Boss : _movingMen[0];
            if (!lead.CopyPlannedRoute(_walkPath) || _walkPath.Count < 2) return;

            var ink = _movingMen.Exists(man => man.Urgent) ? Running : Walking;
            float length = PathLength(_walkPath);
            float fan = Mathf.Min(6f, length * 0.24f);
            float mergeAt = fan;
            float splitAt = Mathf.Max(mergeAt, length - fan);
            var merge = Lift(PointAlong(_walkPath, mergeAt), 0.12f);
            var split = Lift(PointAlong(_walkPath, splitAt), 0.12f);

            foreach (var man in _movingMen)
                Line(ink, Lift(man.Tf.position, 0.12f), merge);

            CopyPathSegment(_walkPath, mergeAt, splitAt, _trunkPath);
            if (_trunkPath.Count > 1)
                Path(ink, _trunkPath, 0.12f);

            foreach (var man in _movingMen)
            {
                var goal = Lift(man.OrderDestination, 0.13f);
                Line(ink, split, goal);
                const float half = 0.28f;
                Ring(ink,
                    goal + new Vector3(half, 0f, half),
                    goal + new Vector3(half, 0f, -half),
                    goal + new Vector3(-half, 0f, -half),
                    goal + new Vector3(-half, 0f, half));
            }
        }

        static Vector3 Lift(Vector3 point, float metres)
        {
            point.y += metres;
            return point;
        }

        static float PathLength(List<Vector3> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }

        static Vector3 PointAlong(List<Vector3> path, float distance)
        {
            if (path.Count == 0) return Vector3.zero;
            distance = Mathf.Max(0f, distance);
            float walked = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float leg = Vector3.Distance(path[i - 1], path[i]);
                if (walked + leg >= distance && leg > 0.001f)
                    return Vector3.Lerp(path[i - 1], path[i], (distance - walked) / leg);
                walked += leg;
            }
            return path[path.Count - 1];
        }

        static void CopyPathSegment(List<Vector3> source, float from, float to,
            List<Vector3> into)
        {
            into.Clear();
            if (source.Count == 0) return;
            from = Mathf.Max(0f, from);
            to = Mathf.Max(from, to);
            AddPathPoint(into, PointAlong(source, from));

            float walked = 0f;
            for (int i = 1; i < source.Count; i++)
            {
                walked += Vector3.Distance(source[i - 1], source[i]);
                if (walked > from && walked < to) AddPathPoint(into, source[i]);
                if (walked >= to) break;
            }
            AddPathPoint(into, PointAlong(source, to));
        }

        static void AddPathPoint(List<Vector3> into, Vector3 point)
        {
            if (into.Count == 0 || (into[into.Count - 1] - point).sqrMagnitude > 0.01f)
                into.Add(point);
        }

        /// <summary>The actual lane route for each car currently driven by the outfit.
        /// Parked cars, empty cars, rival cars and police traffic stay out of the view.</summary>
        void DrawCars()
        {
            foreach (var car in _crews.Cars)
            {
                if (car == null || car.Tf == null || car.Occupant == null ||
                    car.Occupant.Faction != 0) continue;
                if (!car.CopyPlannedRoute(_carPath) || _carPath.Count < 2) continue;

                Path(Driving, _carPath);

                var goal = _carPath[_carPath.Count - 1];
                Ring(Driving,
                    goal + new Vector3(0.45f, 0f, 0.45f),
                    goal + new Vector3(0.45f, 0f, -0.45f),
                    goal + new Vector3(-0.45f, 0f, -0.45f),
                    goal + new Vector3(-0.45f, 0f, 0.45f));
            }
        }

        void Path(Color ink, List<Vector3> points, float lift = 0f)
        {
            int slot = _used;
            var line = Take(ink, points.Count, path: true);
            var vertices = _pathPoints[slot];
            bool changed = _pathCounts[slot] != points.Count;
            if (vertices.Length < points.Count)
                _pathPoints[slot] = vertices = new Vector3[Mathf.Max(points.Count, vertices.Length * 2)];
            for (int i = 0; i < points.Count; i++)
            {
                var point = Lift(points[i], lift);
                changed |= !vertices[i].Equals(point);
                vertices[i] = point;
            }
            // Unity ignores capacity beyond positionCount. Keep unchanged native
            // geometry intact, including when I hides and then restores the same line.
            if (changed) line.SetPositions(vertices);
            _pathCounts[slot] = points.Count;
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

        LineRenderer Take(Color ink, int points, bool path = false)
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
                _pathPoints.Add(System.Array.Empty<Vector3>());
                _pathCounts.Add(0);
            }

            // A pooled renderer reused for a ring/branch no longer contains its path.
            if (!path) _pathCounts[_used] = 0;
            _used++;
            line.enabled = true;
            if (line.positionCount != points) line.positionCount = points;
            line.sharedMaterial = Ink(ink);
            return line;
        }

        Material Ink(Color colour)
        {
            if (_inks.TryGetValue(colour, out var mat)) return mat;
            var urp = Shader.Find("Universal Render Pipeline/Unlit");
            var shader = urp != null ? urp : Shader.Find("Unlit/Color");
            mat = new Material(shader) { name = "Combat Intent " + colour };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", colour);
            _inks[colour] = mat;
            return mat;
        }

        void OnDestroy()
        {
            foreach (var mat in _inks.Values)
                if (mat != null) Destroy(mat);
            _inks.Clear();
            if (_root != null) Destroy(_root.gameObject);
        }
    }
}
