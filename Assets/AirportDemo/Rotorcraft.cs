using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo
{
    /// <summary>
    /// A helicopter: sits on its pad, winds up, picks straight up into the hover,
    /// noses over and goes wherever it is sent, comes back, comes down. Two of them
    /// work the field - the sheriff's, which keeps the pad and flies a patrol, and a
    /// charter that drops in, stands ten minutes and leaves.
    ///
    /// The pack's model carries both rotors as separate meshes with a blurred disc
    /// beside each, which is all the animation a helicopter at this distance needs:
    /// the discs come on with the throttle and the blades go off.
    /// </summary>
    public sealed class Rotorcraft
    {
        enum Phase { Idle, Spool, Lift, Transit, Return, Descend, Down }

        public Transform Tf;
        public bool Resident = true;          // the sheriff's; a charter comes and goes
        public Vector3 Pad;
        public float PadYaw;
        public List<Vector3> Patrol = new List<Vector3>();

        Phase _state = Phase.Down;
        float _timer, _throttle, _rotor, _speed;
        int _leg;
        Transform _mainRotor, _tailRotor;
        float _groundOffset;
        ParticleSystem _wash;

        const float HoverHeight = 26f;
        const float TransitSpeed = 28f;
        const float ClimbRate = 6f;

        /// <summary>Reads the model and scales it to the rotor diameter it flies at:
        /// the Simple Airport pack carries the main disc as "rotor_main" and the tail
        /// one as "rotor_tail", and its import scale is not this project's.</summary>
        public void Bind(Transform tf, float rotorDiameter)
        {
            Tf = tf;
            _mainRotor = AirportKit.FindDeep(tf, "rotor_main") ?? AirportKit.FindDeep(tf, "Blades_01");
            _tailRotor = AirportKit.FindDeep(tf, "rotor_tail") ?? AirportKit.FindDeep(tf, "Blades_02");

            // the disc is the widest thing on a helicopter, so it is what the scale is
            // taken from; if the model has no separate rotor, the fuselage will do
            var rotorBounds = _mainRotor != null
                ? AirportKit.BoundsOf(_mainRotor.gameObject)
                : AirportKit.BoundsOf(tf.gameObject);
            float across = Mathf.Max(rotorBounds.size.x, rotorBounds.size.z);
            if (across > 0.001f) tf.localScale = tf.localScale * (rotorDiameter / across);
            var b = AirportKit.BoundsOf(tf.gameObject);
            _groundOffset = tf.position.y - b.min.y;
        }

        public void SetWash(ParticleSystem wash) => _wash = wash;

        public void Park()
        {
            _state = Phase.Down;
            _timer = Resident ? Random.Range(30f, 90f) : Random.Range(60f, 160f);
            if (Tf != null) Tf.SetPositionAndRotation(Pad + Vector3.up * _groundOffset, Quaternion.Euler(0f, PadYaw, 0f));
        }

        public void Tick(float dt)
        {
            if (Tf == null) return;
            _timer -= dt;
            TickRotor(dt);

            switch (_state)
            {
                case Phase.Down:
                    _throttle = Mathf.MoveTowards(_throttle, 0f, dt * 0.5f);
                    if (_timer <= 0f && Patrol.Count > 0) { _state = Phase.Spool; _timer = 8f; }
                    break;

                case Phase.Spool:
                    _throttle = Mathf.MoveTowards(_throttle, 1f, dt * 0.18f);
                    if (_wash != null && !_wash.isPlaying) { _wash.transform.position = Tf.position; _wash.Play(); }
                    if (_timer <= 0f) { _state = Phase.Lift; _leg = 0; }
                    break;

                case Phase.Lift:
                    {
                        var p = Tf.position;
                        p.y = Mathf.MoveTowards(p.y, Pad.y + _groundOffset + HoverHeight, ClimbRate * dt);
                        Tf.position = p;
                        // she noses over as she climbs away, the way one does
                        float lean = Mathf.Lerp(0f, -8f, (p.y - Pad.y - _groundOffset) / HoverHeight);
                        Tf.rotation = Quaternion.Euler(lean, Tf.eulerAngles.y, 0f);
                        if (p.y >= Pad.y + _groundOffset + HoverHeight - 0.2f)
                        {
                            _state = Phase.Transit;
                            if (_wash != null) _wash.Stop();
                        }
                    }
                    break;

                case Phase.Transit:
                    if (Fly(Patrol[_leg], dt))
                    {
                        _leg++;
                        if (_leg >= Patrol.Count) { _state = Phase.Return; }
                    }
                    break;

                case Phase.Return:
                    if (Fly(new Vector3(Pad.x, Pad.y + _groundOffset + HoverHeight, Pad.z), dt)) _state = Phase.Descend;
                    break;

                case Phase.Descend:
                    {
                        var p = Tf.position;
                        p.y = Mathf.MoveTowards(p.y, Pad.y + _groundOffset, ClimbRate * 0.7f * dt);
                        Tf.position = p;
                        float yaw = Mathf.MoveTowardsAngle(Tf.eulerAngles.y, PadYaw, 40f * dt);
                        Tf.rotation = Quaternion.Euler(Mathf.Lerp(0f, -6f, (p.y - Pad.y - _groundOffset) / HoverHeight), yaw, 0f);
                        if (_wash != null && !_wash.isPlaying && p.y < Pad.y + 12f) { _wash.transform.position = Pad; _wash.Play(); }
                        if (p.y <= Pad.y + _groundOffset + 0.05f)
                        {
                            _state = Phase.Down;
                            _timer = Resident ? Random.Range(60f, 180f) : Random.Range(120f, 260f);
                            if (_wash != null) _wash.Stop();
                        }
                    }
                    break;
            }
        }

        /// <summary>Flies toward a point, banking into the turn. True when it is there.</summary>
        bool Fly(Vector3 goal, float dt)
        {
            var to = goal - Tf.position;
            var flat = new Vector3(to.x, 0f, to.z);
            float dist = flat.magnitude;
            _speed = Mathf.MoveTowards(_speed, dist > 60f ? TransitSpeed : TransitSpeed * 0.4f, 6f * dt);
            float yaw = Tf.eulerAngles.y;
            if (dist > 1f)
            {
                float want = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                float next = Mathf.MoveTowardsAngle(yaw, want, 34f * dt);
                float bank = Mathf.Clamp(Mathf.DeltaAngle(yaw, next) / dt / 34f * 18f, -18f, 18f);
                yaw = next;
                Tf.rotation = Quaternion.Euler(-7f, yaw, -bank);
            }
            var p = Tf.position + Tf.forward * (_speed * dt);
            p.y = Mathf.MoveTowards(Tf.position.y, goal.y, ClimbRate * dt);
            Tf.position = p;
            return dist < Mathf.Max(12f, _speed * 0.6f);
        }

        void TickRotor(float dt)
        {
            if (_state != Phase.Down) _throttle = Mathf.MoveTowards(_throttle, 1f, dt * 0.3f);
            _rotor = (_rotor + _throttle * 900f * dt) % 360f;
            // the main disc turns about its own Y, the tail rotor about its own X
            if (_mainRotor != null) _mainRotor.localRotation = Quaternion.Euler(0f, _rotor, 0f);
            if (_tailRotor != null) _tailRotor.localRotation = Quaternion.Euler(_rotor * 1.6f, 0f, 0f);
        }
    }
}
