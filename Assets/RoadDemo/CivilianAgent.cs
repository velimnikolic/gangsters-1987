using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // A civilian with a life: walks the sidewalk graph like the base agent, but
    // on the way past a bench may turn in and sit a while, and past a door may
    // step inside and vanish - coming back out of another door across the demo
    // later. The door and bench legs are hand-lerped straight off the pavement,
    // the same trick PoliceFootPatrol uses for its forecourt walk. Two civilians
    // meeting head-on may also stop for a chat (PairChats, driven by the builder).
    public class CivilianAgent : PedestrianAgent
    {
        // The authored sit pose, measured off the clips for the city's own
        // pedestrians (PedestrianAnimation) and reused here verbatim: the seated
        // pelvis holds the contact patch SitContactHeight above the root, and the
        // sit-down clip's descent runs SitDescentSeconds.
        const float SitContactHeight = 0.428f;
        const float SitDescentSeconds = 1.2f;
        const float RiseSeconds = 1.1f;

        public enum Mode
        {
            Walking, ToBench, SitDown, Sitting, StandUp, FromBench,
            ToDoor, Inside, WalkOut, Chat,
        }

        public Mode State { get; private set; } = Mode.Walking;

        CityLife _life;
        DemoBench _bench;
        int _seat;
        DemoDoor _door;
        bool _doorFwd;             // which direction the walk-out joins the link in
        PedLink _resumeLink;       // where the bench detour left the graph
        float _resumeT;
        Vector3 _seatRoot;
        Quaternion _sitRot;
        Vector3 _legFrom, _legTo;
        float _legT, _legLen;
        float _timer;
        float _chatCooldown;
        CivilianAgent _partner;

        public void Setup(CityLife life)
        {
            _life = life;
            _chatCooldown = Random.Range(0f, 40f);
        }

        /// <summary>Start the day indoors; the agent must already be Init'd. It
        /// reappears at a random free door once the delay runs out.</summary>
        public void SpawnInside(float delay)
        {
            State = Mode.Inside;
            _timer = delay;
            Tf.gameObject.SetActive(false);
        }

        bool ChatReady => State == Mode.Walking && !_waiting && !_link.Gated
            && _chatCooldown <= 0f && HasPose(PoseTalk);

        public void TickCivilian(float dt)
        {
            switch (State)
            {
                case Mode.Walking:
                {
                    _chatCooldown -= dt;
                    var linkBefore = _link;
                    float tBefore = _t;
                    Tick(dt);
                    // arrival resets _t and _waiting means standing at a kerb -
                    // stops only fire on an uninterrupted stretch of the same link
                    if (!_waiting && _link == linkBefore && _t > tBefore)
                        CheckStops(linkBefore, tBefore, _t);
                    break;
                }

                case Mode.ToBench:
                case Mode.FromBench:
                case Mode.ToDoor:
                case Mode.WalkOut:
                    BlendLocomotion(dt, true);
                    if (TickLeg(dt)) LegDone();
                    break;

                case Mode.SitDown:
                {
                    TickBlend(dt);
                    _timer += dt;
                    // glide over the clip's descent so pose and root arrive together
                    float k = Mathf.SmoothStep(0f, 1f, _timer / SitDescentSeconds);
                    Tf.position = Vector3.Lerp(_legFrom, _seatRoot, k);
                    Tf.rotation = Quaternion.Slerp(Tf.rotation, _sitRot, 6f * dt);
                    if (_timer >= SitDescentSeconds)
                    {
                        Tf.SetPositionAndRotation(_seatRoot, _sitRot);
                        SetPose(PoseSit);
                        State = Mode.Sitting;
                        _timer = Random.Range(_life.SitSeconds.x, _life.SitSeconds.y);
                    }
                    break;
                }

                case Mode.Sitting:
                    TickBlend(dt);
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        RestartPose(PoseStandUp);
                        SetPose(PoseStandUp);
                        State = Mode.StandUp;
                        _timer = 0f;
                    }
                    break;

                case Mode.StandUp:
                {
                    TickBlend(dt);
                    _timer += dt;
                    // standing up puts the feet back under the root: only the height
                    // comes back, the clip carries the pelvis forward over the feet
                    float k = Mathf.SmoothStep(0f, 1f, _timer / RiseSeconds);
                    Tf.position = new Vector3(_seatRoot.x,
                        Mathf.Lerp(_seatRoot.y, _bench.GroundY, k), _seatRoot.z);
                    if (_timer >= RiseSeconds)
                    {
                        _bench.Release(_seat);
                        BeginLeg(Tf.position, LinkPoint(_resumeLink, _resumeT), Mode.FromBench);
                        SetPose(PoseWalk);
                    }
                    break;
                }

                case Mode.Inside:
                    _timer -= dt;
                    if (_timer <= 0f && !TryWalkOut())
                        _timer = 1.5f; // every threshold busy - try again shortly
                    break;

                case Mode.Chat:
                    TickBlend(dt);
                    if (_partner != null)
                    {
                        var look = _partner.Tf.position - Tf.position;
                        look.y = 0f;
                        if (look.sqrMagnitude > 1e-4f)
                            Tf.rotation = Quaternion.Slerp(Tf.rotation,
                                Quaternion.LookRotation(look.normalized), 5f * dt);
                    }
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        State = Mode.Walking;
                        _partner = null;
                        _chatCooldown = Random.Range(30f, 80f);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------- stops

        void CheckStops(PedLink link, float from, float to)
        {
            var stops = _life?.StopsFor(link);
            if (stops == null) return;

            for (int i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                if (stop.T <= from) continue;
                if (stop.T > to) break; // sorted by T

                if (stop.Bench != null && _life.CanSit &&
                    Random.value < _life.SitChance && stop.Bench.TryClaim(out var seat))
                {
                    _bench = stop.Bench;
                    _seat = seat;
                    _resumeLink = _link;
                    _resumeT = _t;
                    BeginLeg(Tf.position, _bench.Approach(seat), Mode.ToBench);
                    return;
                }

                if (stop.Door != null && !stop.Door.Busy &&
                    Random.value < _life.EnterChance)
                {
                    stop.Door.Busy = true;
                    _door = stop.Door;
                    BeginLeg(Tf.position, _door.Pos, Mode.ToDoor);
                    return;
                }
            }
        }

        void LegDone()
        {
            switch (State)
            {
                case Mode.ToBench:
                    RestartPose(PoseSitDown);
                    SetPose(PoseSitDown);
                    _legFrom = Tf.position;
                    // SeatTops names the seat SURFACE; the root goes the pose's
                    // contact height below it, scaled to this rig
                    _seatRoot = _bench.SeatTops[_seat]
                        + Vector3.down * (SitContactHeight * HumanScale);
                    _sitRot = Quaternion.LookRotation(_bench.Facing);
                    State = Mode.SitDown;
                    _timer = 0f;
                    break;

                case Mode.FromBench:
                    _link = _resumeLink;
                    _t = _resumeT;
                    _cameFrom = _resumeLink.From;
                    _bench = null;
                    State = Mode.Walking;
                    break;

                case Mode.ToDoor:
                    // the door heard shutting behind him, at the doorstep he just left
                    DemoAudio.At(DemoSounds.DoorClose, _door.Pos, DemoSounds.DoorVolume, 0.08f);
                    _door.Busy = false;
                    _door = null;
                    State = Mode.Inside;
                    _timer = Random.Range(_life.InsideSeconds.x, _life.InsideSeconds.y);
                    Tf.gameObject.SetActive(false);
                    break;

                case Mode.WalkOut:
                    _door.Busy = false;
                    _link = _doorFwd ? _door.LinkFwd : _door.LinkBack;
                    _t = _doorFwd ? _door.EntryT : _door.LinkFwd.Length - _door.EntryT;
                    _cameFrom = _link.From;
                    _door = null;
                    State = Mode.Walking;
                    break;
            }
        }

        bool TryWalkOut()
        {
            var door = _life?.PickFreeDoor();
            if (door == null) return false;

            door.Busy = true;
            _door = door;
            _doorFwd = Random.value < 0.5f;
            Tf.gameObject.SetActive(true);
            Tf.SetPositionAndRotation(door.Pos, Quaternion.LookRotation(door.Outward));
            DemoAudio.At(DemoSounds.DoorOpen, door.Pos, DemoSounds.DoorVolume, 0.08f);
            BeginLeg(door.Pos, door.EntryPos, Mode.WalkOut);
            SetPose(PoseWalk);
            return true;
        }

        // -------------------------------------------------------------- legs

        void BeginLeg(Vector3 from, Vector3 to, Mode mode)
        {
            State = mode;
            _legFrom = from;
            _legTo = to;
            _legLen = Vector3.Distance(from, to);
            _legT = 0f;
        }

        bool TickLeg(float dt)
        {
            _legT += Speed * dt;
            float f = _legLen < 0.01f ? 1f : Mathf.Clamp01(_legT / _legLen);
            var dir = _legTo - _legFrom;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f)
                Tf.rotation = Quaternion.Slerp(
                    Tf.rotation, Quaternion.LookRotation(dir.normalized), 8f * dt);
            Tf.position = Vector3.Lerp(_legFrom, _legTo, f);
            return f >= 1f;
        }

        static Vector3 LinkPoint(PedLink link, float t)
            => Vector3.Lerp(link.From.Pos, link.To.Pos, t / link.Length);

        // -------------------------------------------------------------- chat

        /// <summary>Two walkers meeting head-on on the same stretch stop for a
        /// word - or, now and then, an argument. Scanned on a slow throttle by
        /// the builder; O(n^2) but only over ChatReady walkers.</summary>
        public static void PairChats(List<CivilianAgent> all, Vector2 chatSeconds)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (!a.ChatReady) continue;

                for (int j = i + 1; j < all.Count; j++)
                {
                    var b = all[j];
                    if (!b.ChatReady) continue;
                    // head-on: b walks the reverse of a's link
                    if (a._link.From != b._link.To || a._link.To != b._link.From) continue;
                    if ((a.Tf.position - b.Tf.position).sqrMagnitude > 2.2f * 2.2f) continue;

                    if (Random.value > 0.4f)
                    {
                        // strangers, this time - and no re-roll every scan while
                        // the two are still passing each other
                        a._chatCooldown = 8f;
                        break;
                    }

                    bool shout = Random.value < 0.15f;
                    float seconds = Random.Range(chatSeconds.x, chatSeconds.y);
                    a.BeginChat(b, shout, seconds);
                    b.BeginChat(a, shout, seconds);
                    break;
                }
            }
        }

        void BeginChat(CivilianAgent partner, bool shout, float seconds)
        {
            _partner = partner;
            State = Mode.Chat;
            _timer = seconds;
            int pose = shout && HasPose(PoseShout) ? PoseShout : PoseTalk;
            RestartPose(pose, Random.value * 0.8f); // desync the two gesticulations
            SetPose(pose);
        }
    }
}
