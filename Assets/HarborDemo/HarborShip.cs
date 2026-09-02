using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // One vessel under way: a bare root the path moves, the model a child that bobs
    // (HarborBob), the wake and funnel smoke children that answer to the speed. The
    // path is a list of legs - straight, a cubic curve, or a crab (a sideways glide
    // that holds the heading, tugs implied) - run by arc length under one speed
    // profile: ease up to each leg's cap, brake so the path's end is met at its end
    // speed, never a jump between legs. Plain class ticked by the shipping.
    public sealed class HarborShip
    {
        public enum Kind { Straight, Curve, Crab }

        public sealed class Leg
        {
            public Kind Type;
            public Vector3 A, B, C0, C1;
            public float Cap = 8f;
            /// <summary>Speed the ship should have where the leg ends (0 = a stop).</summary>
            public float EndSpeed;
            float _len;
            float[] _arc;   // cumulative length at 33 samples of a curve

            public float Length => _len;

            public static Leg Straight(Vector3 a, Vector3 b, float cap, float endSpeed = 0f)
                => new Leg { Type = Kind.Straight, A = a, B = b, Cap = cap, EndSpeed = endSpeed }.Measure();
            public static Leg Crab(Vector3 a, Vector3 b, float cap, float endSpeed = 0f)
                => new Leg { Type = Kind.Crab, A = a, B = b, Cap = cap, EndSpeed = endSpeed }.Measure();
            public static Leg Curve(Vector3 a, Vector3 c0, Vector3 c1, Vector3 b, float cap, float endSpeed = 0f)
                => new Leg { Type = Kind.Curve, A = a, C0 = c0, C1 = c1, B = b, Cap = cap, EndSpeed = endSpeed }.Measure();

            Leg Measure()
            {
                if (Type != Kind.Curve) { _len = (B - A).magnitude; return this; }
                _arc = new float[33];
                var prev = A;
                for (int i = 1; i < 33; i++)
                {
                    var p = Point(i / 32f);
                    _arc[i] = _arc[i - 1] + (p - prev).magnitude;
                    prev = p;
                }
                _len = _arc[32];
                return this;
            }

            Vector3 Point(float t)
            {
                float u = 1f - t;
                return u * u * u * A + 3f * u * u * t * C0 + 3f * u * t * t * C1 + t * t * t * B;
            }

            Vector3 Tangent(float t)
            {
                float u = 1f - t;
                var d = 3f * u * u * (C0 - A) + 6f * u * t * (C1 - C0) + 3f * t * t * (B - C1);
                return d.sqrMagnitude > 1e-6f ? d.normalized : (B - A).normalized;
            }

            /// <summary>Position and heading at a distance along the leg.</summary>
            public void At(float s, out Vector3 pos, out Vector3 dir)
            {
                if (Type != Kind.Curve)
                {
                    float k = _len > 0.001f ? Mathf.Clamp01(s / _len) : 1f;
                    pos = Vector3.Lerp(A, B, k);
                    dir = (B - A).normalized;
                    return;
                }
                // arc length to parameter through the table
                s = Mathf.Clamp(s, 0f, _len);
                int i = 1;
                while (i < 32 && _arc[i] < s) i++;
                float span = _arc[i] - _arc[i - 1];
                float t = ((i - 1) + (span > 1e-5f ? (s - _arc[i - 1]) / span : 0f)) / 32f;
                pos = Point(t);
                dir = Tangent(t);
            }
        }

        public const float Accel = 0.35f;
        public const float Decel = 0.4f;
        public const float YawRate = 8f;   // degrees a second, plenty for a ship's curves

        public Transform Root, Model;
        public HarborShipSpec Spec;
        public HarborBob Bob;
        public float Length, HalfBeam;
        public bool Passer;
        /// <summary>How many of the spec's deck slots, from the first, hold through
        /// cargo - stowed at launch, carried in and out again. The berth's handler
        /// works the slots above these.</summary>
        public int DeckBase;

        readonly List<Leg> _legs = new List<Leg>();
        int _leg;
        float _s, _speed;
        ParticleSystem _wake, _bowWave, _smoke;
        float _wakeRate, _bowRate, _smokeRate;

        public float Speed => _speed;
        public bool Idle => _legs.Count == 0;
        public Vector3 Position => Root.localPosition;
        public float X => Root.localPosition.x;
        /// <summary>Raised once when the path runs out.</summary>
        public System.Action Arrived;

        // ------------------------------------------------------------ making one

        /// <summary>A live vessel: the root at a spot on the water with a heading, the
        /// prefab a child carrying the bob, the FX hung on where the spec says.</summary>
        public static HarborShip Launch(GameObject prefab, HarborShipSpec spec, Vector3 position, Vector3 heading,
                                        Transform parent, System.Random rng, float bobScale,
                                        GameObject rippleFx, GameObject smokeFx, string name)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = position;
            root.localRotation = Quaternion.LookRotation(heading, Vector3.up);
            var model = Object.Instantiate(prefab, root);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            HarborKit.StripBehaviours(model, keepAnimator: false);
            var bob = model.AddComponent<HarborBob>();
            bob.Configure((float)rng.NextDouble() * 20f, bobScale);

            var b = HarborKit.PrefabBounds(prefab);
            var ship = new HarborShip
            {
                Root = root, Model = model.transform, Spec = spec, Bob = bob,
                Length = spec != null ? spec.Length : b.size.z,
                HalfBeam = (spec != null ? spec.Beam : b.size.x) * 0.5f,
            };
            float stern = spec != null ? spec.SternZ + 0.5f : b.min.z + 0.5f;
            float bow = spec != null ? spec.BowZ - 1.5f : b.max.z - 1.5f;
            if (rippleFx != null)
            {
                ship._wake = Fx(rippleFx, root, new Vector3(0f, 0.05f, stern), 2.6f);
                ship._bowWave = Fx(rippleFx, root, new Vector3(0f, 0.05f, bow), 1.6f);
            }
            if (smokeFx != null && spec != null)
            {
                ship._smoke = Fx(smokeFx, model.transform, spec.FunnelTop, 1.4f);
                LivingCity.Ambient.FireSmokeFx.TintSmoke(
                    ship._smoke, LivingCity.Ambient.FireSmokeFx.DieselSmoke);
            }
            ship.SetFx(0f);
            return ship;
        }

        static ParticleSystem Fx(GameObject prefab, Transform parent, Vector3 local, float scale)
        {
            var go = Object.Instantiate(prefab, parent);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * scale;
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps == null) return null;
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            if (!ps.isPlaying) ps.Play();
            return ps;
        }

        void SetFx(float speed)
        {
            float k = Mathf.Clamp01(speed / 8f);
            Rate(_wake, k < 0.03f ? 0f : 4f + 22f * k, ref _wakeRate);
            Rate(_bowWave, k < 0.05f ? 0f : 3f + 12f * k, ref _bowRate);
            Rate(_smoke, 2f + 8f * k, ref _smokeRate);
        }

        static void Rate(ParticleSystem ps, float rate, ref float last)
        {
            if (ps == null || Mathf.Abs(rate - last) < 0.2f) return;
            last = rate;
            var em = ps.emission;
            em.rateOverTime = rate;
        }

        // ------------------------------------------------------------ the path

        public void SetPath(IEnumerable<Leg> legs)
        {
            _legs.Clear();
            _legs.AddRange(legs);
            _leg = 0;
            _s = 0f;
        }

        public void Extend(Leg leg) => _legs.Add(leg);

        public void Stop()
        {
            _legs.Clear();
            _leg = 0;
            _s = 0f;
        }

        /// <summary>Metres of path left ahead of the ship.</summary>
        public float Remaining()
        {
            if (_legs.Count == 0) return 0f;
            float r = _legs[_leg].Length - _s;
            for (int i = _leg + 1; i < _legs.Count; i++) r += _legs[i].Length;
            return r;
        }

        public void Tick(float dt)
        {
            if (_legs.Count == 0)
            {
                _speed = Mathf.MoveTowards(_speed, 0f, Decel * dt);
                SetFx(_speed);
                return;
            }
            var leg = _legs[_leg];

            // the speed: up to this leg's cap, down in time for the end of the path
            // (its end speed) and for any slower leg ahead
            float want = leg.Cap;
            float ahead = leg.Length - _s;
            float endSpeed = _legs[_legs.Count - 1].EndSpeed;
            float toEnd = ahead;
            for (int i = _leg + 1; i < _legs.Count; i++)
            {
                float allowed = Mathf.Sqrt(_legs[i].Cap * _legs[i].Cap + 2f * Decel * Mathf.Max(0f, toEnd - 0.5f));
                want = Mathf.Min(want, allowed);
                toEnd += _legs[i].Length;
            }
            want = Mathf.Min(want, Mathf.Sqrt(endSpeed * endSpeed + 2f * Decel * Mathf.Max(0f, toEnd - 0.5f)));
            _speed = Mathf.MoveTowards(_speed, want, (want > _speed ? Accel : Decel) * dt);

            _s += _speed * dt;
            while (_s >= leg.Length)
            {
                _s -= leg.Length;
                _leg++;
                if (_leg >= _legs.Count)
                {
                    // the path's end: land exactly on it, keep the heading
                    Root.localPosition = leg.B;
                    if (leg.Type != Kind.Crab)
                    {
                        leg.At(leg.Length, out _, out var endDir);
                        Root.localRotation = Quaternion.LookRotation(endDir, Vector3.up);
                    }
                    _legs.Clear();
                    _leg = 0;
                    _s = 0f;
                    _speed = endSpeed;
                    SetFx(_speed);
                    Arrived?.Invoke();
                    return;
                }
                leg = _legs[_leg];
            }

            leg.At(_s, out var pos, out var dir);
            Root.localPosition = pos;
            if (leg.Type != Kind.Crab)
            {
                var wantRot = Quaternion.LookRotation(dir, Vector3.up);
                Root.localRotation = Quaternion.RotateTowards(Root.localRotation, wantRot, YawRate * dt);
            }
            SetFx(_speed);
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            Root = null;
        }
    }
}
