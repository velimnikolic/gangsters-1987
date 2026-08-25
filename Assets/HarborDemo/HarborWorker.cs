using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // A man with a job on the quay: walks a round of points - the gangway's foot,
    // the pallet pile, the shed door, a smoke by the drum - stands a while at each,
    // walks on. The road demo's walker base drives the body (walk and idle blended
    // through its playable graph); this only says where to go and moves the
    // transform, the same trick the crews use on their empty floor. A deckhand is
    // the same man with his points in his ship's frame - parented to the model, he
    // rides its heave and goes where it goes.
    public sealed class HarborWorker : RoadDemo.PedestrianAgent
    {
        public Transform Frame;          // null: world points; a ship's model: deck points
        public List<Vector3> Points = new List<Vector3>();
        public Vector2 DwellRange = new Vector2(2.5f, 8f);
        /// <summary>A man who stands at his post (the captain on his wing).</summary>
        public bool Static;

        int _target;
        float _dwell;
        bool _walking;

        // ---------------------------------------------------------------- the break
        //
        // A port works in shifts and a shift takes its break. A man called off his
        // round walks to a seat - a crate by the burn barrel, a pallet at the door -
        // sits on it for a few minutes and goes back to his points. Everything below
        // is the civilian's bench sequence cut down to what a docker needs: no bench
        // to book, no link to resume, and the seat named as a world point rather than
        // as a slot on a piece of furniture.

        enum Rest { None, Going, Down, Sat, Up }

        /// <summary>The pelvis holds its contact patch this far above the root in the
        /// library's sit, and the descent and the rise take this long - CivilianAgent's
        /// figures, off the same clips.</summary>
        const float SitContactHeight = 0.428f, SitDescentSeconds = 1.2f, RiseSeconds = 1.1f;

        Rest _rest;
        Vector3 _seatTop, _seatRoot, _standFrom;
        float _restTimer, _restFor, _seatYaw, _groundY;

        /// <summary>Whether he is off his round - the shift boss asks before calling
        /// another man off it.</summary>
        public bool Resting => _rest != Rest.None;

        /// <summary>Call him off his round to a seat: the top surface of the thing he
        /// sits on, which way he faces on it, and for how long. Refused - and he simply
        /// carries on - if his body has no sit clips.</summary>
        public bool TakeBreak(Vector3 seatTop, float yaw, float seconds)
        {
            if (Tf == null || _rest != Rest.None) return false;
            if (!HasPose(PoseSitDown) || !HasPose(PoseSit)) return false;
            _seatTop = seatTop;
            _seatYaw = yaw;
            _restFor = seconds;
            _groundY = Tf.position.y;
            _rest = Rest.Going;
            _walking = true;
            return true;
        }

        /// <summary>One frame of a man on his break. True while he is still on it.</summary>
        bool TickRest(float dt)
        {
            switch (_rest)
            {
                case Rest.Going:
                {
                    // up to the seat, stopping a stride short of it - the sit clip
                    // carries him the last of the way down onto it
                    var goal = _seatTop;
                    var to = goal - Tf.position;
                    to.y = 0f;
                    float dist = to.magnitude;
                    if (dist > 0.45f)
                    {
                        var step = to / dist * Mathf.Min(dist, Speed * dt);
                        Tf.position += new Vector3(step.x, 0f, step.z);
                        Tf.rotation = Quaternion.Slerp(Tf.rotation, Quaternion.LookRotation(to / dist, Vector3.up), dt * 6f);
                        BlendLocomotion(dt, true);
                        return true;
                    }
                    RestartPose(PoseSitDown);
                    SetPose(PoseSitDown);
                    _standFrom = Tf.position;
                    _seatRoot = _seatTop + Vector3.down * (SitContactHeight * HumanScale);
                    _restTimer = 0f;
                    _rest = Rest.Down;
                    return true;
                }
                case Rest.Down:
                {
                    TickBlend(dt);
                    _restTimer += dt;
                    float k = Mathf.SmoothStep(0f, 1f, _restTimer / SitDescentSeconds);
                    Tf.position = Vector3.Lerp(_standFrom, _seatRoot, k);
                    Tf.rotation = Quaternion.Slerp(Tf.rotation, Quaternion.Euler(0f, _seatYaw, 0f), 6f * dt);
                    if (_restTimer >= SitDescentSeconds)
                    {
                        Tf.SetPositionAndRotation(_seatRoot, Quaternion.Euler(0f, _seatYaw, 0f));
                        SetPose(PoseSit);
                        _rest = Rest.Sat;
                        _restTimer = _restFor;
                    }
                    return true;
                }
                case Rest.Sat:
                    TickBlend(dt);
                    _restTimer -= dt;
                    if (_restTimer <= 0f)
                    {
                        RestartPose(PoseStandUp);
                        SetPose(PoseStandUp);
                        _rest = Rest.Up;
                        _restTimer = 0f;
                    }
                    return true;
                case Rest.Up:
                {
                    TickBlend(dt);
                    _restTimer += dt;
                    // standing up puts the feet back under the root: only the height
                    // comes back, the clip carries the pelvis over them
                    float k = Mathf.SmoothStep(0f, 1f, _restTimer / RiseSeconds);
                    Tf.position = new Vector3(_seatRoot.x, Mathf.Lerp(_seatRoot.y, _groundY, k), _seatRoot.z);
                    if (_restTimer >= RiseSeconds)
                    {
                        _rest = Rest.None;
                        _walking = false;
                        _dwell = HarborKit.Range(_rng, 1f, 3f);
                        SetPose(PoseIdle);
                    }
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>After InitAt: stands him at one of his points and starts his round.
        /// (Speed is read by the base at InitAt - set it before.)</summary>
        public void Begin()
        {
            _target = 0;
            _dwell = HarborKit.Range(_rng, 0.5f, 3f);
            if (Points.Count > 0)
            {
                _target = _rng.Next(Points.Count);
                Tf.position = Resolve(Points[_target]);
                _target = (_target + 1) % Points.Count;
            }
        }

        static readonly System.Random _rng = new System.Random(7);

        Vector3 Resolve(Vector3 p) => Frame != null ? Frame.TransformPoint(p) : p;

        public new void Tick(float dt)
        {
            if (Tf == null) return;
            if (_rest != Rest.None && TickRest(dt)) return;
            if (Static || Points.Count == 0)
            {
                BlendLocomotion(dt, false);
                return;
            }
            if (!_walking)
            {
                _dwell -= dt;
                if (_dwell <= 0f)
                {
                    _walking = true;
                    if (Points.Count > 2 && _rng.NextDouble() < 0.3)
                        _target = _rng.Next(Points.Count);   // not always the next one round
                }
                BlendLocomotion(dt, false);
                return;
            }
            var goal = Resolve(Points[_target]);
            var pos = Tf.position;
            var to = goal - pos;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.25f)
            {
                _walking = false;
                _dwell = HarborKit.Range(_rng, DwellRange.x, DwellRange.y);
                _target = (_target + 1) % Points.Count;
                BlendLocomotion(dt, false);
                return;
            }
            var step = to / dist * Mathf.Min(dist, Speed * dt);
            var next = pos + step;
            next.y = goal.y;
            Tf.position = next;
            var face = Quaternion.LookRotation(to / dist, Vector3.up);
            Tf.rotation = Quaternion.Slerp(Tf.rotation, face, dt * 6f);
            BlendLocomotion(dt, true);
        }
    }
}
