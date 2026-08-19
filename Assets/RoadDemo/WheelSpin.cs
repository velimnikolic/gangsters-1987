using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The wheels of whatever body it is handed, turned where they actually sit. The
    // packs model a vehicle in one piece: every part - door, lid, wheel - keeps the
    // vehicle's own origin for a pivot, so turning a wheel part on its own axis swings
    // it around the middle of the car instead of around its axle. On a six-wheel truck
    // the two rear pairs then orbit a shared centre like one fat wheel. So the hub of
    // each wheel is read off its mesh and the part is spun about that, its pivot slid
    // over by as much as the spin moved the hub - which leaves a wheel that is already
    // pivoted at its hub (any pack that models them that way) exactly where it was.
    public sealed class WheelSpin
    {
        struct Wheel
        {
            public Transform Tf;
            public Vector3 Rest;      // pivot at rest, in the body's frame
            public Quaternion Turn;   // rotation at rest
            public Vector3 Hub;       // rest pivot -> hub, in the body's frame
            public Vector3 Spoke;     // the same offset, in the wheel's own frame
            public float Along;       // where the hub sits front to back
            public int Named;         // what the name says: 1 front, 0 rear, -1 nothing
            public bool Front;        // steers with the wheel
        }

        readonly List<Wheel> _wheels = new List<Wheel>();
        float _radius = 0.33f, _spin, _steerShown;

        /// <summary>How many wheels the body turned out to have.</summary>
        public int Count => _wheels.Count;

        /// <summary>The wheel radius read off the meshes - what the roll is measured
        /// against.</summary>
        public float Radius => _radius;

        /// <summary>Where the rear and front hubs sit along the body (its own z), for
        /// whoever steers it: the rear axle is what follows a path, the front swings.
        /// NaN without wheels.</summary>
        public float RearAxle { get; private set; } = float.NaN;
        public float FrontAxle { get; private set; } = float.NaN;

        /// <summary>The wheel parts of a body, and which of them steer: the packs name
        /// them Wheel_fl / Wheel_LF / Wheel_rl_01 - an f in the side token is a front
        /// wheel, and a name that says nothing is judged by where it sits.</summary>
        public void Read(Transform body)
        {
            _wheels.Clear();
            _steerShown = 0f;
            float radius = 0f, front = float.MinValue, back = float.MaxValue;
            RearAxle = FrontAxle = float.NaN;

            foreach (var t in body.GetComponentsInChildren<Transform>(true))
            {
                if (t == body || t.name.IndexOf("Wheel", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (t.name.IndexOf("Steering", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (Inside(t)) continue;          // a part of a wheel already taken: it rides along
                if (!Hub(t, out var b)) continue; // nothing drawn: nothing to turn

                var hub = t.parent ? t.parent.InverseTransformPoint(b.center) : b.center;
                var w = new Wheel
                {
                    Tf = t,
                    Rest = t.localPosition,
                    Turn = t.localRotation,
                    Hub = hub - t.localPosition,
                    Along = body.InverseTransformPoint(b.center).z,
                    Named = Side(t.name),
                };
                w.Spoke = Quaternion.Inverse(w.Turn) * w.Hub;
                w.Front = w.Named == 1;
                _wheels.Add(w);

                radius = Mathf.Max(radius, b.extents.y);
                front = Mathf.Max(front, w.Along);
                back = Mathf.Min(back, w.Along);
            }

            // Whatever the names left open: the wheels in the front half of the
            // wheelbase are the pair that turns into the corner.
            float mid = (front + back) * 0.5f;
            for (int i = 0; i < _wheels.Count; i++)
            {
                var w = _wheels[i];
                if (w.Named < 0) { w.Front = w.Along > mid; _wheels[i] = w; }
            }

            _radius = Mathf.Max(0.2f, radius);
            if (_wheels.Count > 0) { RearAxle = back; FrontAxle = front; }
        }

        /// <summary>Rolling with the road, the front pair turned into the corner.</summary>
        public void Tick(float dt, float speed, float steerDegrees)
        {
            if (_wheels.Count == 0) return;
            _spin = (_spin + speed * dt / _radius * Mathf.Rad2Deg) % 360f;
            _steerShown = Mathf.MoveTowards(_steerShown, Mathf.Clamp(steerDegrees, -32f, 32f), 160f * dt);
            for (int i = 0; i < _wheels.Count; i++)
            {
                var w = _wheels[i];
                if (!w.Tf) continue;
                var turn = w.Front ? Quaternion.AngleAxis(_steerShown, Vector3.up) : Quaternion.identity;
                var q = turn * w.Turn * Quaternion.AngleAxis(_spin, Vector3.right);
                w.Tf.localRotation = q;
                // the pivot goes wherever it has to for the hub to stay on its axle
                if (w.Hub != Vector3.zero) w.Tf.localPosition = w.Rest + w.Hub - q * w.Spoke;
            }
        }

        // Where the wheel is drawn, in the world - its middle is the hub, half its
        // height the tyre. Off the mesh rather than off Renderer.bounds, so a part that
        // is switched off (a livery variant, a spare) still measures true.
        static bool Hub(Transform wheel, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var mf in wheel.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var box = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = box.center + new Vector3(
                        (c & 1) == 0 ? -box.extents.x : box.extents.x,
                        (c & 2) == 0 ? -box.extents.y : box.extents.y,
                        (c & 4) == 0 ? -box.extents.z : box.extents.z);
                    var p = mf.transform.TransformPoint(corner);
                    if (any) b.Encapsulate(p);
                    else { b = new Bounds(p, Vector3.zero); any = true; }
                }
            }
            return any;
        }

        // A wheel hung under a wheel (a hubcap, a tyre part) turns with it, so it must
        // not be turned twice - parents come first out of the hierarchy walk.
        bool Inside(Transform t)
        {
            for (var p = t.parent; p; p = p.parent)
                for (int i = 0; i < _wheels.Count; i++)
                    if (_wheels[i].Tf == p) return true;
            return false;
        }

        // The corner a part name puts the wheel in - Wheel_fl, Wheel_LF, Wheel_rl_01,
        // Wheel_Front_L - read from the back of the name past any numbering. Only the
        // tokens that plainly say it count; anything else is left to the geometry.
        static int Side(string name)
        {
            var parts = name.Split('_');
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var token = parts[i].ToLowerInvariant();
                if (token == "front") return 1;
                if (token == "rear" || token == "back") return 0;
                if (token.Length != 2 || !Corner(token[0]) || !Corner(token[1])) continue;
                return token[0] == 'f' || token[1] == 'f' ? 1 : 0;
            }
            return -1;
        }

        // The letters a corner token is made of: fl, fr, rl, rr and their reversals.
        static bool Corner(char c) => c == 'f' || c == 'r' || c == 'l';
    }
}
