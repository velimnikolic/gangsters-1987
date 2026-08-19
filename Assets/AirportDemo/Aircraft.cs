using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo
{
    /// <summary>
    /// One aeroplane. A plain class the builder ticks - no physics, no rigidbody: it
    /// flies a queue of legs, each with a point to reach and a speed to reach it at,
    /// and turns toward the next one at a rate it is allowed. That is enough for
    /// everything an aeroplane does at a regional field - taxi a yellow line, hold,
    /// line up, roll, climb, fly the circuit, come down final and roll out - and it
    /// never puts two aircraft in the same place, because the queue is handed out by
    /// FlightOps, which owns the runway and the taxiways.
    ///
    /// The model is a Simple Airport one (the only aircraft in the project; see the
    /// note in AirportKit about why nothing else comes from that pack). It arrives
    /// nose on +Z with its wheels as separate meshes, and is scaled here to the span
    /// its class is given in AirportSpec - which is the only thing that makes a
    /// trijet read as a trijet next to a Cessna on the same ramp.
    /// </summary>
    public sealed class Aircraft
    {
        public enum Phase
        {
            Parked, StartUp, Taxi, Hold, LineUp, Roll, Climb, Cruise, Final, Rollout, Shutdown, Away
        }

        /// <summary>What it is, which decides its size, its speeds and where it parks.</summary>
        public enum Kind { Light, Commuter, Jet }

        public struct Leg
        {
            public Vector3 To;
            public float Speed;
            /// <summary>On the ground it keeps its wheels on the concrete and turns
            /// like a tricycle; in the air it banks into the turn.</summary>
            public bool Ground;
        }

        public Transform Tf;
        public Phase State = Phase.Parked;
        public Kind Class = Kind.Light;
        public string Callsign = "N1987";
        /// <summary>Which stand or tie-down this aeroplane belongs to.</summary>
        public int Stand = -1;
        /// <summary>A scheduled aeroplane works the timetable and takes passengers;
        /// the light ones come and go as they please.</summary>
        public bool Commuter;
        /// <summary>Seconds left before whatever it is waiting for.</summary>
        public float Timer;
        /// <summary>Set by FlightOps while this aeroplane holds a runway clearance.</summary>
        public bool HasRunway;
        /// <summary>Flying the circuit rather than leaving: it comes round onto final
        /// under its own power instead of going off the map and coming back.</summary>
        public bool InCircuit;

        /// <summary>Speeds for this class, filled in by FlightOps when it is adopted.</summary>
        public float RotateSpeed = AirportSpec.GaRotate;
        public float ClimbSpeed = AirportSpec.GaClimb;
        public float ApproachSpeed = AirportSpec.GaApproach;
        /// <summary>How far down the runway it needs before it will fly.</summary>
        public float TakeoffRun = 380f;

        /// <summary>Measured off the model after it was scaled: half the span, the
        /// spinner ahead of the origin and the rudder behind it.</summary>
        public float HalfSpan { get; private set; } = 5.5f;
        public float Nose { get; private set; } = 4f;
        public float Tail { get; private set; } = -4f;

        public Vector3 Position => Tf != null ? Tf.position : Vector3.zero;
        public Vector3 Forward => Tf != null ? Tf.forward : Vector3.forward;
        public float Speed => _speed;
        public bool Idle => _legs.Count == 0;
        /// <summary>Where the aeroplane is going, for whoever wants to keep out of it.</summary>
        public Vector3 Goal => _legs.Count > 0 ? _legs[0].To : Position;

        readonly List<Leg> _legs = new List<Leg>();
        float _speed;
        float _bank;
        float _throttle;          // 0 shut down, 1 full - what the propellers read
        float _spin;

        readonly List<Transform> _wheels = new List<Transform>();
        readonly List<float> _wheelRadius = new List<float>();
        readonly List<Transform> _props = new List<Transform>();
        readonly List<Quaternion> _propRest = new List<Quaternion>();
        float _groundOffset;      // model origin above the wheels' underside
        ParticleSystem _touchdownFx, _startFx;

        const float TaxiAccel = 1.6f, TaxiBrake = 2.6f;
        const float RollAccel = 3.6f, RollBrake = 3.2f;
        const float GroundTurnRate = 24f;    // degrees a second at taxi speed
        const float AirTurnRate = 8f;
        const float MaxBank = 24f;

        // ------------------------------------------------------------ building

        /// <summary>Reads the model: scales it to the span its class flies at, finds
        /// the wheels and the propellers, and works out how far its origin stands
        /// above the ground so it can be set down on the concrete.</summary>
        public void Bind(Transform tf, float targetSpan)
        {
            Tf = tf;
            // measure it square and at the origin: the bounds are world-space, and a
            // prefab that arrives turned or offset would be measured across its
            // diagonal and scaled wrong
            tf.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var raw = AirportKit.BoundsOf(tf.gameObject);
            float span = Mathf.Max(0.001f, raw.size.x);
            float k = targetSpan / span;
            tf.localScale = tf.localScale * k;

            var b = AirportKit.BoundsOf(tf.gameObject);
            HalfSpan = b.size.x * 0.5f;
            Nose = b.max.z - tf.position.z;
            Tail = b.min.z - tf.position.z;
            _groundOffset = tf.position.y - b.min.y;

            // the undercarriage: two mains and a nose wheel, each its own mesh
            var found = new List<Transform>();
            AirportKit.FindAllDeep(tf, "Wheel", found);
            foreach (var w in found)
            {
                var wb = AirportKit.BoundsOf(w.gameObject);
                if (wb.size.y < 0.001f) continue;
                _wheels.Add(w);
                _wheelRadius.Add(Mathf.Max(0.12f, wb.size.y * 0.5f));
            }
            // the propellers, on the nose or out on the wings. The name has to START
            // with it: the commuter's own fuselage mesh is called "Plane_Propellor01",
            // and a contains-test would spin the whole aeroplane about its nose.
            found.Clear();
            AirportKit.FindAllDeep(tf, "ropell", found);
            foreach (var p in found)
            {
                var n = p.name.ToLowerInvariant();
                if (!n.StartsWith("propell")) continue;
                if (_props.Contains(p)) continue;
                _props.Add(p);
                _propRest.Add(p.localRotation);
            }
        }

        public void SetEffects(ParticleSystem touchdown, ParticleSystem start)
        {
            _touchdownFx = touchdown;
            _startFx = start;
        }

        // ------------------------------------------------------------ orders

        public void Clear() => _legs.Clear();

        public void Go(Vector3 to, float speed, bool ground = true)
            => _legs.Add(new Leg { To = to, Speed = speed, Ground = ground });

        public void GoAll(IEnumerable<Vector3> points, float speed, bool ground = true)
        {
            foreach (var p in points) Go(p, speed, ground);
        }

        /// <summary>Puts the aeroplane down at a point facing a heading, with no
        /// business anywhere - how a parked one is set out on its stand. The point is
        /// the ground under it; the model is lifted onto its wheels.</summary>
        public void Park(Vector3 at, float yaw)
        {
            Clear();
            _speed = 0f;
            _throttle = 0f;
            State = Phase.Parked;
            if (Tf != null) Tf.SetPositionAndRotation(new Vector3(at.x, at.y + _groundOffset, at.z), Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>Kept so the machinery reads the same for a model with doors and
        /// one without: these aircraft have none that open.</summary>
        public void Doors(bool open) { }

        /// <summary>Throttle, which is all the propellers and the sound care about.</summary>
        public void Throttle(float t) => _throttle = Mathf.Clamp01(t);

        // ------------------------------------------------------------ the tick

        public void Tick(float dt)
        {
            if (Tf == null) return;
            TickSpin(dt);
            if (_legs.Count == 0)
            {
                _speed = Mathf.MoveTowards(_speed, 0f, TaxiBrake * dt);
                if (_speed > 0.01f) Tf.position += Tf.forward * (_speed * dt);
                _bank = Mathf.MoveTowards(_bank, 0f, 30f * dt);
                ApplyBank();
                return;
            }

            var leg = _legs[0];
            var pos = Tf.position;
            var to = leg.To - pos;
            float planar = new Vector2(to.x, to.z).magnitude;

            // how fast: ease down into the last of a ground leg so a turn is taken at
            // taxi speed and a landing rollout does not end in a skid
            float want = leg.Speed;
            if (leg.Ground && _legs.Count == 1)
                want = Mathf.Min(want, Mathf.Sqrt(Mathf.Max(0.5f, planar) * 2f * TaxiBrake));
            else if (leg.Ground && _legs.Count > 1 && planar < 40f)
            {
                var next = _legs[1].To - leg.To;
                float turn = Vector3.Angle(new Vector3(to.x, 0f, to.z), new Vector3(next.x, 0f, next.z));
                if (turn > 25f) want = Mathf.Min(want, AirportSpec.TaxiTurnSpeed);
            }
            float accel = State == Phase.Roll ? RollAccel : (leg.Ground ? TaxiAccel : RollAccel);
            float brake = State == Phase.Rollout ? RollBrake : (leg.Ground ? TaxiBrake : RollBrake);
            _speed = _speed < want ? Mathf.MoveTowards(_speed, want, accel * dt)
                                   : Mathf.MoveTowards(_speed, want, brake * dt);

            // where to point: the heading to the next point, at the rate allowed
            if (planar > 0.05f)
            {
                float targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float yaw = Tf.eulerAngles.y;
                float rate = leg.Ground
                    ? GroundTurnRate * Mathf.Clamp01(0.35f + _speed / AirportSpec.TaxiSpeed * 0.65f)
                    : AirTurnRate;
                float next = Mathf.MoveTowardsAngle(yaw, targetYaw, rate * dt);
                float delta = Mathf.DeltaAngle(yaw, next);
                Tf.rotation = Quaternion.Euler(0f, next, 0f);
                float wantBank = leg.Ground ? 0f : Mathf.Clamp(delta / Mathf.Max(dt, 0.001f) / AirTurnRate * MaxBank, -MaxBank, MaxBank);
                _bank = Mathf.MoveTowards(_bank, wantBank, 40f * dt);
            }

            // move, and take the climb or descent of the leg with it
            var step = Tf.forward * (_speed * dt);
            var p = pos + step;
            if (leg.Ground) p.y = leg.To.y + _groundOffset;
            else
            {
                float remain = Mathf.Max(planar, 0.01f);
                float rise = (leg.To.y + _groundOffset - pos.y) * Mathf.Clamp01(step.magnitude / remain);
                p.y = pos.y + rise;
            }
            Tf.position = p;
            ApplyPitch(leg, planar);

            float reach = leg.Ground ? Mathf.Max(2f, _speed * 0.35f) : Mathf.Max(20f, _speed * 0.9f);
            if (planar <= reach) _legs.RemoveAt(0);
        }

        void ApplyPitch(Leg leg, float planar)
        {
            float pitch = 0f;
            if (!leg.Ground)
            {
                float rise = leg.To.y + _groundOffset - Tf.position.y;
                pitch = -Mathf.Clamp(Mathf.Atan2(rise, Mathf.Max(planar, 1f)) * Mathf.Rad2Deg, -8f, 12f);
            }
            var e = Tf.eulerAngles;
            Tf.rotation = Quaternion.Euler(pitch, e.y, -_bank);
        }

        void ApplyBank()
        {
            var e = Tf.eulerAngles;
            Tf.rotation = Quaternion.Euler(e.x, e.y, -_bank);
        }

        /// <summary>The wheels roll at the speed the aeroplane is doing, and the
        /// propellers turn with the throttle - a disc at anything above idle, which is
        /// what a propeller looks like.</summary>
        void TickSpin(float dt)
        {
            for (int i = 0; i < _wheels.Count; i++)
            {
                if (_wheels[i] == null) continue;
                float degrees = _speed / _wheelRadius[i] * Mathf.Rad2Deg * dt;
                _wheels[i].Rotate(Vector3.right, degrees, Space.Self);
            }
            if (_props.Count == 0) return;
            _spin = (_spin + Mathf.Lerp(0f, 2400f, _throttle) * dt) % 360f;
            for (int i = 0; i < _props.Count; i++)
                if (_props[i] != null) _props[i].localRotation = _propRest[i] * Quaternion.Euler(0f, 0f, _spin);
        }

        /// <summary>The puff of dust the wheels make on contact, and the one the
        /// engines kick up when they are started on a dusty ramp.</summary>
        public void Puff(bool touchdown)
        {
            var fx = touchdown ? _touchdownFx : _startFx;
            if (fx == null || Tf == null) return;
            fx.transform.position = Tf.position + Tf.forward * (touchdown ? Tail : Nose);
            fx.Play();
        }

        public void Show(bool on)
        {
            if (Tf != null && Tf.gameObject.activeSelf != on) Tf.gameObject.SetActive(on);
        }
    }
}
