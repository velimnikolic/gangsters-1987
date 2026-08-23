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
    //
    // And a nerve: a shot heard close by freezes him for a beat and then sends him
    // running down the pavement away from it (across a crossing without waiting on
    // the light, if that is the way out); heard a street off, he stops, looks, and
    // makes off the other way, or ducks into the nearest door, or gets down behind
    // whatever is nearest until it is over. A stray round can find him. When it has
    // been quiet a while a few drift back to stand and stare at the chalk.
    public class CivilianAgent : PedestrianAgent
    {
        // The authored sit pose, measured off the clips for the city's own
        // pedestrians (PedestrianAnimation) and reused here verbatim: the seated
        // pelvis holds the contact patch SitContactHeight above the root, and the
        // sit-down clip's descent runs SitDescentSeconds.
        const float SitContactHeight = 0.428f;
        const float SitDescentSeconds = 1.2f;
        const float RiseSeconds = 1.1f;

        // The nerve's distances: a shot this close and he bolts at once; this far
        // and he freezes, then bolts, hides or gets down; farther and he only makes
        // off the other way. Fear fades at FearFade a second once the street is quiet.
        const float CloseShot = 15f;
        const float NearShot = 45f;
        const float FearFade = 1f / 25f;
        const float BodyStays = 5f;    // seconds a fallen civilian lies before the chalk

        public enum Mode
        {
            Walking, ToBench, SitDown, Sitting, StandUp, FromBench,
            ToDoor, Inside, WalkOut, Chat,
            Startle, Flee, Cower, Gawk, Dead,
        }

        public Mode State { get; private set; } = Mode.Walking;

        /// <summary>Two hits and a bystander is down.</summary>
        public int Health = 2;
        public bool Dead => State == Mode.Dead;

        /// <summary>Somebody to step round: not once he is down. The body lies where it
        /// fell for a while before it is struck off, and for that while the pavement
        /// used to steer the whole crowd round a corpse as though it were a bin.</summary>
        protected override bool InCrowd => !Dead;

        /// <summary>How frightened, 0..1 - above a little and he takes no benches,
        /// no doors of his own choosing and no chats.</summary>
        public float Fear => _fear;

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

        // the nerve
        float _fear;
        Vector3 _threat;           // where the last shot he heard came from
        int _zone = 2;             // how close the worst of it was: 0 close, 1 near, 2 far
        float _walkSpeed, _runSpeed;
        float _hurryUntil;         // walking quick after a scare until then
        float _fleeMetres;         // ground still to cover at the run
        bool _hideWanted;          // take the next door, whatever the odds
        bool _pendingPanic;        // heard it sat down / on a leg: react once back on the pavement
        float _flinch;
        float _deadAt;
        bool _bodyGone;
        Vector3? _gawkAt;          // the scene he is drifting over to stare at
        float _gawkStop;

        // ------------------------------------------------------------ the crowd

        /// <summary>Every civilian in the scene - the arena's list of bystanders a
        /// stray round can find, and the traffic's picture of who is in the road.</summary>
        public static readonly List<CivilianAgent> All = new List<CivilianAgent>();

        static bool listening;
        static float lastScream = -10f, gawkScan;

        /// <summary>When a frightened civilian last got indoors - to a telephone.</summary>
        public static float LastHidAt { get; private set; } = -1000f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            All.Clear();
            listening = false;
            lastScream = -10f;
            gawkScan = 0f;
            LastHidAt = -1000f;
        }

        static void Listen()
        {
            if (listening) return;
            listening = true;
            StreetAlarm.OnShot += HearAll;
        }

        static void HearAll(StreetAlarm.Shot shot)
        {
            for (int i = 0; i < All.Count; i++) All[i].Hear(shot);
        }

        public void Setup(CityLife life)
        {
            _life = life;
            _chatCooldown = Random.Range(0f, 40f);
            _walkSpeed = Speed;
            _runSpeed = Random.Range(3.0f, 4.2f);
            if (!All.Contains(this)) All.Add(this);
            Listen();
        }

        /// <summary>Start the day indoors; the agent must already be Init'd. It
        /// reappears at a random free door once the delay runs out.</summary>
        public void SpawnInside(float delay)
        {
            State = Mode.Inside;
            _timer = delay;
            Tf.gameObject.SetActive(false);
            Suspend(true);
        }

        bool ChatReady => State == Mode.Walking && !_waiting && !_link.Gated
            && _chatCooldown <= 0f && _fear < 0.15f && HasPose(PoseTalk) && !NearGate();

        /// <summary>Metres of pavement kept clear of a crossing's mouth: a pair
        /// stopped for a word right at the kerb line stands, to anyone watching,
        /// in the middle of the zebra.</summary>
        const float GateBerth = 4f;

        bool NearGate()
        {
            if (_t < GateBerth && HasGate(_link.From)) return true;
            if (_link.Length - _t < GateBerth && HasGate(_link.To)) return true;
            return false;
        }

        static bool HasGate(PedNode n)
        {
            for (int i = 0; i < n.Links.Count; i++)
                if (n.Links[i].Gated) return true;
            return false;
        }

        /// <summary>Out on the pavement (or the road), alive - somebody a round can hit.</summary>
        public bool Exposed => !Dead && Tf != null && Tf.gameObject.activeSelf &&
                               State != Mode.Inside;

        /// <summary>On the carriageway right now - running a crossing, or crouched
        /// down off the kerb - where the traffic must reckon with him.</summary>
        public bool OnCarriageway => Exposed && _link != null && _link.Gated && !_waiting &&
                                     (State == Mode.Flee || State == Mode.Walking || State == Mode.Cower);

        public void TickCivilian(float dt)
        {
            // the body is gone - chalk laid, rig suspended - so there is nothing left to
            // tick. Without this every one of the dead ran the whole state switch again
            // each frame for the rest of the session: the crowd's per-frame cost never
            // fell as it was killed, which is the freeze that built up over a violent
            // night. He is also struck from All the moment the body goes (Mode.Dead).
            if (_bodyGone) return;

            if (_fear > 0f && State != Mode.Flee && State != Mode.Startle)
                _fear = Mathf.Max(0f, _fear - FearFade * dt * (StreetAlarm.QuietFor > 6f ? 1f : 0.2f));

            switch (State)
            {
                case Mode.Walking:
                {
                    _chatCooldown -= dt;
                    if (_hurryUntil > 0f && Time.time > _hurryUntil) { _hurryUntil = 0f; SetWalkPace(_walkSpeed); }
                    if (_pendingPanic) { _pendingPanic = false; BeginStartle(0.1f, 0.4f); break; }
                    var linkBefore = _link;
                    float tBefore = _t;
                    Tick(dt);
                    // arrival resets _t and _waiting means standing at a kerb -
                    // stops only fire on an uninterrupted stretch of the same link
                    if (!_waiting && _link == linkBefore && _t > tBefore)
                        CheckStops(linkBefore, tBefore, _t);
                    else if (_waiting) TickWaitLife(dt);
                    if (_gawkAt.HasValue) TickGawkApproach();
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
                        _timer = _pendingPanic ? 0f : Random.Range(_life.SitSeconds.x, _life.SitSeconds.y);
                    }
                    break;
                }

                case Mode.Sitting:
                    TickBlend(dt);
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        RestartPose(PoseStandUp, 0f, _pendingPanic ? 1.6f : 1f);
                        SetPose(PoseStandUp);
                        State = Mode.StandUp;
                        _timer = 0f;
                    }
                    break;

                case Mode.StandUp:
                {
                    TickBlend(dt);
                    _timer += dt;
                    float rise = _pendingPanic ? RiseSeconds * 0.65f : RiseSeconds;
                    // standing up puts the feet back under the root: only the height
                    // comes back, the clip carries the pelvis forward over the feet
                    float k = Mathf.SmoothStep(0f, 1f, _timer / rise);
                    Tf.position = new Vector3(_seatRoot.x,
                        Mathf.Lerp(_seatRoot.y, _bench.GroundY, k), _seatRoot.z);
                    if (_timer >= rise)
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
                    // the sidestep out of the walking line, position and lateral in
                    // step, so a chat cut short resumes from where he really stands
                    Tf.position = Vector3.MoveTowards(Tf.position, _chatSlide, 0.7f * dt);
                    Lateral = Mathf.MoveTowards(Lateral, _chatLatTo, 0.7f * dt);
                    if (_partner != null)
                        TurnToward(_partner.Tf.position - Tf.position, 150f, dt);
                    // the hands go with the word: a point up the street, a shrug, a
                    // nod at what the other one just said
                    if (!Acting && !Joining && (_gestureIn -= dt) <= 0f)
                    {
                        _gestureIn = Random.Range(2.5f, 6f);
                        PlayAction(Random.value < 0.5f
                            ? CrewKit.SpeakGestures : CrewKit.ListenGestures);
                    }
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        State = Mode.Walking;
                        _partner = null;
                        _chatCooldown = Random.Range(30f, 80f);
                    }
                    break;

                // ---------------------------------------------------- the nerve

                case Mode.Startle:
                {
                    // frozen a beat, turned toward the bang (or finishing the flinch of
                    // the round that hit him); then he decides
                    if (_flinch > 0f) { _flinch -= dt; SetPose(PoseHit); }
                    else
                    {
                        SetPose(PoseIdle);
                        FaceToward(_threat, 6f * dt);
                    }
                    TickBlend(dt);
                    _timer -= dt;
                    if (_timer <= 0f) Decide();
                    break;
                }

                case Mode.Flee:
                {
                    var before = Tf.position;
                    Tick(dt);
                    var moved = Tf.position - before;
                    moved.y = 0f;
                    _fleeMetres -= moved.magnitude;
                    if (_fleeMetres <= 0f && StreetAlarm.Danger(Tf.position) < 0.08f)
                        EndFlee();
                    break;
                }

                case Mode.Cower:
                {
                    // some get down behind the nearest thing; some go down on their
                    // knees where they stand, which is 1987 and is also the only way
                    // a crowd under fire reads as people rather than as one reflex
                    if (!_kneeling || !HoldAction(CrewKit.PrayKneel, 0.5f))
                        SetPose(HasPose(PoseCrouch) ? PoseCrouch : PoseIdle);
                    TickBlend(dt);
                    var away = Tf.position - _threat;
                    away.y = 0f;
                    if (away.sqrMagnitude > 1e-3f)
                        Tf.rotation = Quaternion.Slerp(Tf.rotation, Quaternion.LookRotation(away.normalized), 4f * dt);
                    _timer -= dt;
                    if (_timer <= 0f && StreetAlarm.QuietFor > 3f)
                    {
                        // up, and off the other way at a quick walk
                        State = Mode.Walking;
                        Hurry(20f);
                        SetPose(PoseWalk);
                    }
                    break;
                }

                case Mode.Gawk:
                {
                    TickBlend(dt);
                    // stood staring: a calm man with time to turn properly, so he
                    // steps round to the scene rather than swivelling to it
                    if (_gawkAt.HasValue)
                        TurnToward(_gawkAt.Value - Tf.position, 90f, dt);
                    if (!Acting && (_gawkChat -= dt) <= 0f)
                    {
                        _gawkChat = Random.Range(4f, 11f);
                        PlayAction(CrewKit.ListenGestures);
                    }
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        _gawkAt = null;
                        State = Mode.Walking;
                        SetPose(PoseWalk);
                    }
                    break;
                }

                case Mode.Dead:
                    TickBlend(dt);
                    if (HasPose(PoseDeath) && PoseTime(PoseDeath) >= PoseLength(PoseDeath) - 0.03f)
                        HoldPose(PoseDeath);
                    if (!_bodyGone && Time.time - _deadAt > BodyStays)
                    {
                        _bodyGone = true;
                        // struck off the crowd roll: All is scanned every frame (the
                        // road-walker list, the gawk pick, the blast) and a corpse has no
                        // business in any of those scans. Terminal - a dead civilian is
                        // never revived (the SetActive(true) path is the go-indoors one).
                        All.Remove(this);
                        CrewGore.Chalk(this, Tf.position.y);
                        Tf.gameObject.SetActive(false);
                        Suspend(true);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------- stops

        void CheckStops(PedLink link, float from, float to)
        {
            var stops = _life?.StopsFor(link);
            if (stops == null) return;
            if (_fear > 0.15f && !_hideWanted) return; // no bench, no browsing, with that going on
            if (_gawkAt.HasValue) return;

            for (int i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                if (stop.T <= from) continue;
                if (stop.T > to) break; // sorted by T

                if (stop.Bench != null && _life.CanSit && !_hideWanted &&
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
                    (_hideWanted || Random.value < _life.EnterChance))
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
                    // hiding from the shooting, he stays in a good while - and, being 1987,
                    // that is where the telephone is (the dispatcher reads LastHidAt)
                    bool hiding = _hideWanted || _fear > 0.3f;
                    if (hiding) LastHidAt = Time.time;
                    _timer = hiding
                        ? Random.Range(30f, 90f)
                        : Random.Range(_life.InsideSeconds.x, _life.InsideSeconds.y);
                    _hideWanted = false;
                    _pendingPanic = false;
                    Calm();
                    Tf.gameObject.SetActive(false);
                    Suspend(true);
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
            // still shooting outside: he stays put
            if (StreetAlarm.IncidentOpen && StreetAlarm.QuietFor < 8f) return false;
            var door = _life?.PickFreeDoor();
            if (door == null) return false;

            door.Busy = true;
            _door = door;
            _doorFwd = Random.value < 0.5f;
            Tf.gameObject.SetActive(true);
            Suspend(false);
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
            // GaitGain is the join's: he eases up to his pace over the start clip
            // rather than sliding off the pavement with his feet still planted
            _legT += Speed * GaitGain * dt;
            float f = _legLen < 0.01f ? 1f : Mathf.Clamp01(_legT / _legLen);
            var dir = _legTo - _legFrom;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f && !Joining)
                Tf.rotation = Quaternion.Slerp(
                    Tf.rotation, Quaternion.LookRotation(dir.normalized), 8f * dt);
            Tf.position = Vector3.Lerp(_legFrom, _legTo, f);
            return f >= 1f;
        }

        /// <summary>The line a start clip is chosen against: the leg he is about to
        /// walk - in to a bench, out of a door - not the stretch he stands on.</summary>
        protected override Vector3 JoinHeading
        {
            get
            {
                if (State == Mode.ToBench || State == Mode.FromBench ||
                    State == Mode.ToDoor || State == Mode.WalkOut)
                {
                    var to = _legTo - Tf.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 1e-4f) return to;
                }
                return base.JoinHeading;
            }
        }

        static Vector3 LinkPoint(PedLink link, float t)
            => Vector3.Lerp(link.From.Pos, link.To.Pos, t / link.Length);

        // -------------------------------------------------------------- chat

        /// <summary>Two walkers meeting head-on on the same stretch stop for a
        /// word - or, now and then, an argument. Scanned on a slow throttle by
        /// the builder; O(n^2) but only over ChatReady walkers.</summary>
        // scratch for PairChats: the ready walkers, and who is on which stretch,
        // kept between scans so a scan over three hundred walkers allocates nothing
        static readonly List<CivilianAgent> Ready = new List<CivilianAgent>();
        static readonly Dictionary<PedLink, List<CivilianAgent>> OnLink = new Dictionary<PedLink, List<CivilianAgent>>();
        static readonly Stack<List<CivilianAgent>> Spare = new Stack<List<CivilianAgent>>();

        public static void PairChats(List<CivilianAgent> all, Vector2 chatSeconds)
        {
            // bucket the ready walkers by the stretch they are on: a meeting is two
            // walkers on the two directions of one stretch, so only those are compared
            Ready.Clear();
            foreach (var kv in OnLink) { kv.Value.Clear(); Spare.Push(kv.Value); }
            OnLink.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (!a.ChatReady) continue;
                Ready.Add(a);
                if (!OnLink.TryGetValue(a._link, out var list))
                {
                    list = Spare.Count > 0 ? Spare.Pop() : new List<CivilianAgent>();
                    OnLink[a._link] = list;
                }
                list.Add(a);
            }

            for (int i = 0; i < Ready.Count; i++)
            {
                var a = Ready[i];
                if (!a.ChatReady) continue;
                // the reverse of a's stretch, and whoever walks it
                var back = ReverseOf(a._link);
                if (back == null || !OnLink.TryGetValue(back, out var facing)) continue;

                for (int j = 0; j < facing.Count; j++)
                {
                    var b = facing[j];
                    if (b == a || !b.ChatReady) continue;
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
                    StepAside(a, b);
                    break;
                }
            }
        }

        /// <summary>The other direction of a stretch: the link from its To back to its From.</summary>
        static PedLink ReverseOf(PedLink link)
        {
            var links = link.To.Links;
            for (int k = 0; k < links.Count; k++)
                if (links[k].To == link.From) return links[k];
            return null;
        }

        void BeginChat(CivilianAgent partner, bool shout, float seconds)
        {
            _partner = partner;
            State = Mode.Chat;
            _timer = seconds;
            _chatSlide = Tf.position;
            _chatLatTo = Lateral;
            int pose = shout && HasPose(PoseShout) ? PoseShout : PoseTalk;
            RestartPose(pose, Random.value * 0.8f); // desync the two gesticulations
            SetPose(pose);
            _gestureIn = Random.Range(1.5f, 4f);
            // it opens the way one opens - a nod at the other man
            if (!shout) PlayAction(CrewKit.Greet);
        }

        // The little life: how long until his next gesture, fidget or glance. One
        // clock each, so nobody's beat is anybody else's.
        float _gestureIn, _fidgetIn = -1f, _gawkChat;
        bool _kneeling;

        /// <summary>How much of the crowd goes down on its KNEES under fire rather
        /// than into the crouch, and how many of the men stood at a red light have
        /// had a few. Both small: what they are for is the one man in a queue who is
        /// not doing what the other nine are.</summary>
        const float PrayChance = 0.3f, DrunkChance = 0.06f, SnackChance = 0.25f;

        /// <summary>One in five with a gun going off beside him puts his hands up at
        /// whoever is holding it, and stands there a beat longer for having done it.</summary>
        const float PleadChance = 0.2f;

        /// <summary>What a man does with a red light. He has thirty seconds and
        /// nothing to do with them: he checks his watch, shifts his weight, has a
        /// look up the street, finishes what he bought at the counter - and one in
        /// twenty is holding the lamp post up. Nothing at all while he is frightened
        /// (a scared man at a kerb is watching the street, not his watch), and
        /// nothing for a man too far off to be read (PlayAction's own gate).</summary>
        void TickWaitLife(float dt)
        {
            if (Acting || Joining || _fear > 0.2f) return;
            if (_fidgetIn < 0f) _fidgetIn = Random.Range(1.5f, 5f);
            if ((_fidgetIn -= dt) > 0f) return;
            _fidgetIn = Random.Range(4f, 10f);
            if (Random.value < DrunkChance &&
                HoldAction(CrewKit.DrunkSway, Random.Range(5f, 12f))) return;
            PlayAction(Random.value < SnackChance ? CrewKit.Snacks : CrewKit.Fidgets);
        }

        // where the pair edges to for its word, and the lateral that spot is on -
        // kept in step with the slide so a chat broken off mid-step resumes the
        // walk from the line the man actually stands on, no snap
        Vector3 _chatSlide;
        float _chatLatTo;

        /// <summary>Two stopped for a word do not stand in the middle of the walk:
        /// the pair edges aside together - the same world direction, so they stay
        /// face to face - onto whichever flank of the pavement has the room, and
        /// their laterals go with them so the walk resumes from where they stand.
        /// No room either side (furniture both ways), and they stay put.</summary>
        static void StepAside(CivilianAgent a, CivilianAgent b)
        {
            var dir = a.LinkDirection;
            var right = new Vector3(dir.z, 0f, -dir.x);
            // headroom inside the lateral band, either way; b walks the reverse
            // link, so the same world shift runs against his lateral
            float plus = Mathf.Min(1.2f, 1.9f - a.Lateral, 1.9f + b.Lateral);
            float minus = Mathf.Min(1.2f, 1.9f + a.Lateral, 1.9f - b.Lateral);
            for (int i = 0; i < 2; i++)
            {
                // the roomier flank first
                float s = (plus >= minus) == (i == 0) ? plus : -minus;
                if (Mathf.Abs(s) < 0.5f) continue;
                var off = right * s;
                if (WalkObstacles.Standing(a.Tf.position + off, WalkObstacles.Radius)) continue;
                if (WalkObstacles.Standing(b.Tf.position + off, WalkObstacles.Radius)) continue;
                a._chatSlide = a.Tf.position + off;
                b._chatSlide = b.Tf.position + off;
                a._chatLatTo = a.Lateral + s;
                b._chatLatTo = b.Lateral - s;
                return;
            }
        }

        // ---------------------------------------------------------- the nerve

        // A shot went off. How he takes it depends on how close it was and what he
        // was doing: on the pavement he freezes and then reacts; sat down or on a
        // leg off the graph he finishes getting back on it first (quickly); on his
        // way in through a door he keeps going - that is the best place to be.
        void Hear(StreetAlarm.Shot shot)
        {
            if (Dead || Tf == null) return;
            if (State == Mode.Inside)
            {
                _timer = Mathf.Max(_timer, Random.Range(20f, 40f)); // not coming out into that
                return;
            }
            var delta = shot.Pos - Tf.position;
            delta.y = 0f;
            float d = delta.magnitude;
            if (d > shot.Loudness) return;

            float fear = Mathf.Clamp01(1.15f - d / shot.Loudness);
            if (fear > _fear) _fear = fear;
            _threat = shot.Pos;
            int zone = d < CloseShot ? 0 : d < NearShot ? 1 : 2;

            switch (State)
            {
                case Mode.Startle:
                    // closer than the one he froze at: he makes his mind up quicker
                    if (zone < _zone) { _zone = zone; _timer = Mathf.Min(_timer, 0.4f); }
                    return;

                case Mode.Flee:
                    // it is ahead of him now: turn and run the other way
                    if (d < 25f && Vector3.Dot(LinkDirection, delta) > 0.3f * d) ReverseCourse();
                    _fleeMetres = Mathf.Max(_fleeMetres, 30f);
                    return;

                case Mode.Cower:
                    if (zone == 0) { _zone = 0; BeginStartle(0.1f, 0.3f); }
                    else _timer = Mathf.Max(_timer, Random.Range(3f, 8f));
                    return;

                case Mode.Gawk:
                    _gawkAt = null;
                    _zone = 0;
                    BeginStartle(0.1f, 0.4f);
                    return;

                case Mode.Chat:
                    _partner = null;
                    _chatCooldown = Random.Range(30f, 80f);
                    _zone = zone;
                    BeginStartle(zone == 0 ? 0.15f : 0.4f, zone == 0 ? 0.6f : 1.2f);
                    return;

                case Mode.Walking:
                    _gawkAt = null;
                    _zone = zone;
                    if (zone == 2)
                    {
                        // a street off: a look round, then on his way - the other way
                        BeginStartle(0.8f, 2f);
                        return;
                    }
                    BeginStartle(zone == 0 ? 0.15f : 0.4f, zone == 0 ? 0.6f : 1.2f);
                    return;

                case Mode.ToDoor:
                    // nearly in: run for it
                    if (zone < 2) { Speed = _runSpeed; PickRun(zone == 0); TieRun(); }
                    _hideWanted = true;
                    return;

                case Mode.ToBench:
                    // turn back to the pavement and take it from there
                    _bench.Release(_seat);
                    _bench = null;
                    _pendingPanic = true;
                    _zone = zone;
                    BeginLeg(Tf.position, LinkPoint(_resumeLink, _resumeT), Mode.FromBench);
                    if (zone < 2) { Speed = _runSpeed; PickRun(zone == 0); TieRun(); }
                    return;

                case Mode.Sitting:
                    _timer = 0f;
                    _pendingPanic = true;
                    _zone = zone;
                    return;

                case Mode.SitDown:
                case Mode.StandUp:
                case Mode.FromBench:
                case Mode.WalkOut:
                    _pendingPanic = true;
                    _zone = zone;
                    return;
            }
        }

        void BeginStartle(float atLeast, float atMost)
        {
            State = Mode.Startle;
            _timer = Random.Range(atLeast, atMost);
            _pendingPanic = false;
            _fidgetIn = -1f;
            if (_zone < 2 && Random.value < 0.5f) Scream();
            // The beat of taking it in has a face on it: a look thrown toward the
            // bang, and now and then - only for one that went off beside him - the
            // hands up at whoever is holding the gun, which he stands there long
            // enough to have actually done.
            if (_zone == 0 && Random.value < PleadChance && PlayAction(CrewKit.Plead))
                _timer = Mathf.Max(_timer, 1.2f);
            else if (_zone < 2) PlayAction(CrewKit.ScaredLooks);
        }

        // The beat is over: run, hide, get down, or just make off - by how close it was.
        void Decide()
        {
            _flinch = 0f;
            switch (_zone)
            {
                case 0:
                    BeginFlee(Random.Range(30f, 50f));
                    return;
                case 1:
                {
                    float roll = Random.value;
                    if (roll < 0.55f) { BeginFlee(Random.Range(25f, 40f)); return; }
                    if (roll < 0.85f && TryHide()) return;
                    if (roll >= 0.85f && HasPose(PoseCrouch) && _link != null && !_link.Gated) { BeginCower(); return; }
                    BeginFlee(Random.Range(25f, 40f));
                    return;
                }
                default:
                    // a street off: away from it at a quick walk; one in five ducks in somewhere
                    State = Mode.Walking;
                    if (_link != null && Vector3.Dot(LinkDirection, _threat - Tf.position) > 0f && Random.value < 0.6f)
                        ReverseCourse();
                    _hideWanted = Random.value < 0.2f;
                    Hurry(20f);
                    SetPose(PoseWalk);
                    return;
            }
        }

        void BeginFlee(float metres)
        {
            State = Mode.Flee;
            _fleeMetres = metres;
            _pendingPanic = false;
            if (_link != null)
            {
                var toThreat = _threat - Tf.position;
                toThreat.y = 0f;
                float d = toThreat.magnitude;
                // the shooting is up the pavement ahead: run back the way he came
                if (d > 0.1f && d < 30f && Vector3.Dot(LinkDirection, toThreat) > 0.2f * d) ReverseCourse();
                if (_waiting) Reroute(_link.From);
            }
            Speed = _runSpeed;
            PickRun(_zone == 0);
            TieRun();
            ScatterPhase(_runPose);
        }

        void EndFlee()
        {
            State = Mode.Walking;
            LocomotionPose = PoseWalk;
            Hurry(20f);
            SetPose(PoseWalk);
        }

        /// <summary>Which run this scare is worth. A shot a street away is jogged
        /// from; one that went off beside him is SPRINTED from, flat out - which is
        /// the whole reason the sprint clip is in the wardrobe. Picked once when the
        /// bolt starts, because a man does not change his mind about how hard he is
        /// running halfway down a pavement.</summary>
        int _runPose = PoseJog;

        void PickRun(bool desperate)
        {
            _runPose = desperate && HasPose(PoseSprint) ? PoseSprint
                     : HasPose(PoseJog) ? PoseJog : PoseWalk;
            LocomotionPose = _runPose;
        }

        /// <summary>Metres a second the chosen run clip covers at playback 1.</summary>
        float RunNatural => ClipPace(_runPose, _runPose == PoseSprint ? 5.2f : 3f);

        // The pace at the run: the run clip keeps step; a runner on the walk clip
        // has it played quicker.
        void TieRun()
        {
            if (_runPose != PoseWalk && HasPose(_runPose))
            {
                // the clip's own pace, within the band the feet read right in - a sprint
                // clip is not played at half speed, the man just runs quicker on it
                float natural = RunNatural;
                float rate = Mathf.Clamp(Speed / natural, 0.85f, 1.25f);
                Speed = rate * natural;
                SetPoseSpeed(_runPose, rate);
            }
            HoldWalkRate(Speed);
        }

        /// <summary>TieRun set the jog's rate once, at the bolt - but the ground the
        /// graph then carries her over is braked by whoever is in the way, so a
        /// runner stuck behind slower backs strode at full rate over a quarter of
        /// the pace: the overstriding skate. Re-tied every frame instead, and under
        /// the band the run clip reads in she drops to the walk for those strides
        /// (its rate follows any pace) and bolts again when the way opens. A touch
        /// more asked to re-enter the run than to stay in it, so the gait does not
        /// flicker at the band's edge.</summary>
        protected override void GearLocomotion(float speed)
        {
            if (State == Mode.Flee && _runPose != PoseWalk && HasPose(_runPose))
            {
                float natural = RunNatural;
                bool jog = speed >= (LocomotionPose == _runPose ? 0.85f : 0.95f) * natural;
                LocomotionPose = jog ? _runPose : PoseWalk;
                if (jog) SetPoseSpeed(_runPose, Mathf.Clamp(speed / natural, 0.85f, 1.25f));
                else HoldWalkRate(speed);
                return;
            }
            base.GearLocomotion(speed);
        }

        /// <summary>A bolting civilian gets the runner's read of the pavement too -
        /// the line past the furniture read further out, the crowd's shove at half
        /// strength - for the same reason the crews do: the walk's figures at a
        /// run's pace are a woman weaving lamp to lamp.</summary>
        protected override float FreeLineAhead =>
            State == Mode.Flee ? 4f : base.FreeLineAhead;
        protected override float PushGain =>
            State == Mode.Flee ? 0.5f : base.PushGain;

        void SetWalkPace(float speed)
        {
            Speed = speed;
            LocomotionPose = PoseWalk;
            HoldWalkRate(Speed);
        }

        /// <summary>Walking quick for a while - the pace of somebody who wants to be
        /// somewhere else.</summary>
        void Hurry(float seconds)
        {
            SetWalkPace(_walkSpeed * 1.3f);
            _hurryUntil = Time.time + seconds;
        }

        void Calm()
        {
            SetWalkPace(_walkSpeed);
            _hurryUntil = 0f;
        }

        // The nearest door on this stretch in the direction AWAY from the shooting,
        // within a short run: he goes for it, and stays in a good while.
        bool TryHide()
        {
            if (_life == null || _link == null || _link.Gated) return false;
            var stops = _life.StopsFor(_link);
            if (stops == null) return false;
            var away = Tf.position - _threat;
            away.y = 0f;
            float along = Vector3.Dot(LinkDirection, away); // + : away is up the link
            DemoDoor best = null;
            float bestD = 25f;
            for (int i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                if (stop.Door == null || stop.Door.Busy) continue;
                float dt = stop.T - _t;
                if (Mathf.Sign(dt) != Mathf.Sign(along) && Mathf.Abs(dt) > 4f) continue; // the wrong way
                float d = Mathf.Abs(dt);
                if (d < bestD) { bestD = d; best = stop.Door; }
            }
            if (best == null) return false;
            best.Busy = true;
            _door = best;
            _hideWanted = true;
            Speed = _runSpeed;
            PickRun(_zone == 0);
            TieRun();
            BeginLeg(Tf.position, best.Pos, Mode.ToDoor);
            return true;
        }

        void BeginCower()
        {
            State = Mode.Cower;
            _timer = Random.Range(3f, 8f);
            _kneeling = CrewKit.PrayKneel != null && Random.value < PrayChance;
            ScatterPhase(PoseCrouch);
        }

        void FaceToward(Vector3 point, float k)
        {
            var to = point - Tf.position;
            to.y = 0f;
            if (to.sqrMagnitude > 1e-3f)
                Tf.rotation = Quaternion.Slerp(Tf.rotation, Quaternion.LookRotation(to.normalized), k);
        }

        void Scream()
        {
            if (Time.time - lastScream < 1.2f || DemoSounds.Screams.Length == 0) return;
            lastScream = Time.time;
            // A near-unity pitch now: the list is gasps and yells recorded as gasps and
            // yells, where it used to be hurt grunts that only read as panic transposed
            // up a third - and a real scream taken up a third is a cartoon.
            DemoAudio.At(DemoSounds.Pick(DemoSounds.Screams), Tf.position, DemoSounds.ScreamVolume,
                pitchJitter: 0.12f, pitch: Random.Range(0.95f, 1.12f));
        }

        /// <summary>A crossing is a crossing - unless he is running for his life, when
        /// he takes it against the light and the traffic must brake for him.</summary>
        protected override bool MayEnter(PedLink link)
        {
            if (link.Gated && (State == Mode.Flee || _fear > 0.7f)) return true;
            return base.MayEnter(link);
        }

        // Where the pavement forks: away from the shooting while he is frightened
        // (the link that gains most ground from it), toward the scene when he is
        // drifting over to stare; the plain wander otherwise.
        protected override PedLink ChooseLink(PedNode node, PedNode keepAwayFrom)
        {
            bool away = State == Mode.Flee || _fear > 0.15f;
            bool toward = !away && _gawkAt.HasValue;
            if (!away && !toward) return base.ChooseLink(node, keepAwayFrom);

            var goal = away ? _threat : _gawkAt.Value;
            var to = goal - node.Pos;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-3f) return base.ChooseLink(node, keepAwayFrom);
            to.Normalize();
            float sign = away ? -1f : 1f;

            PedLink pick = null;
            float total = 0f;
            for (int i = 0; i < node.Links.Count; i++)
            {
                var l = node.Links[i];
                var d = l.To.Pos - node.Pos;
                d.y = 0f;
                if (d.sqrMagnitude < 1e-3f) continue;
                float dot = Vector3.Dot(d.normalized, to) * sign; // + : the right way
                float w = 0.04f + Mathf.Max(0f, dot + 0.15f);
                if (l.Gated) w *= away && _fear > 0.6f ? 0.8f : 0.3f;
                if (keepAwayFrom != null && l.To == keepAwayFrom) w *= 0.3f;
                total += w;
                if (Random.value * total <= w) pick = l;
            }
            return pick ?? base.ChooseLink(node, keepAwayFrom);
        }

        // ---------------------------------------------------------- the round

        /// <summary>The first bystander stood in a round's way: within
        /// <paramref name="width"/> of the line from the muzzle out to
        /// <paramref name="reach"/> metres. Null when the round finds nobody.</summary>
        public static CivilianAgent InLine(Vector3 muzzle, Vector3 dir, float reach, float width)
        {
            CivilianAgent best = null;
            float bestAlong = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (!c.Exposed) continue;
                var p = c.Tf.position + Vector3.up * 1.0f;
                var rel = p - muzzle;
                float along = Vector3.Dot(rel, dir);
                if (along < 1.5f || along > reach) continue;
                float off = (rel - dir * along).magnitude;
                if (off > width) continue;
                if (along < bestAlong) { bestAlong = along; best = c; }
            }
            return best;
        }

        /// <summary>A round found him. Enough of them and he goes down.</summary>
        public void TakeHit(int damage, Vector3 from)
        {
            if (Dead) return;
            Health -= Mathf.Max(1, damage);
            _threat = from;
            _fear = 1f;
            if (Health <= 0)
            {
                Kill();
                return;
            }
            LeaveFurniture();
            _zone = 0;
            State = Mode.Startle;
            _pendingPanic = false;
            if (HasPose(PoseHit))
            {
                RestartPose(PoseHit, 0f, Random.Range(0.9f, 1.2f));
                _flinch = Mathf.Min(PoseLength(PoseHit), 0.9f) * 0.8f;
                _timer = _flinch + Random.Range(0.1f, 0.3f);
            }
            else _timer = 0.3f;
            Scream();
        }

        public void Kill()
        {
            if (Dead) return;
            Health = 0;
            LeaveFurniture();
            CancelJoin();   // nobody finishes a turn on the way down
            State = Mode.Dead;
            _deadAt = Time.time;
            _gawkAt = null;
            if (HasPose(PoseDeath))
            {
                RestartPose(PoseDeath, 0f, Random.Range(0.8f, 1.15f));
                Tf.rotation *= Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f);
                SetPose(PoseDeath);
            }
            else
            {
                Tf.rotation *= Quaternion.Euler(-90f, 0f, 0f);
                SetPose(PoseIdle);
            }
            StreetAlarm.Death(Tf.position, StreetAlarm.DeathOf.Civilian);
        }

        // A bench claimed or a door held is given back when he is shot on his way to it.
        void LeaveFurniture()
        {
            if (_bench != null) { _bench.Release(_seat); _bench = null; }
            if (_door != null) { _door.Busy = false; _door = null; }
            _partner = null;
        }

        // ---------------------------------------------------------- the crowd

        /// <summary>The scene's business, once a frame from whoever ticks the crowd:
        /// the traffic's list of who is stood in the road, and - once it has been
        /// quiet a while after a killing - a few of the nearest drift over to stare.</summary>
        public static void TickCrowd(float dt)
        {
            StreetTraffic.Walkers.Clear();
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (c.OnCarriageway) StreetTraffic.Walkers.Add(c.Tf.position);
            }

            gawkScan -= dt;
            if (gawkScan > 0f) return;
            gawkScan = 3f;
            if (StreetAlarm.Deaths == 0) return;
            float quiet = StreetAlarm.QuietFor;
            if (quiet < 25f || quiet > 400f) return;
            int want = Mathf.Min(8, 3 + StreetAlarm.Deaths * 2), have = 0;
            for (int i = 0; i < All.Count; i++)
                if (All[i]._gawkAt.HasValue) have++;
            if (have >= want) return;

            var scene = StreetAlarm.Incident;
            CivilianAgent pick = null;
            int seen = 0;
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (c.State != Mode.Walking || c._gawkAt.HasValue || c._fear > 0.3f || !c.Exposed) continue;
                float d = (c.Tf.position - scene).sqrMagnitude;
                if (d > 60f * 60f || d < 10f * 10f) continue;
                if (Random.Range(0, ++seen) == 0) pick = c;
            }
            if (pick == null) return;
            pick._gawkAt = scene;
            pick._gawkStop = Random.Range(8f, 14f);
            pick.Hurry(60f);
        }

        // On his way over: near enough, and on a pavement (not stood on a crossing),
        // he stops and stares.
        void TickGawkApproach()
        {
            if (!_gawkAt.HasValue || _link == null || _link.Gated || _waiting) return;
            var to = _gawkAt.Value - Tf.position;
            to.y = 0f;
            if (to.magnitude > _gawkStop) return;
            State = Mode.Gawk;
            _timer = Random.Range(60f, 120f);
            Calm();
            SetPose(HasPose(PoseTalk) && Random.value < 0.4f ? PoseTalk : PoseIdle);
        }

        /// <summary>The police moving the crowd back: everyone stood staring within
        /// <paramref name="radius"/> of the spot walks off, and stays off.</summary>
        public static void MoveAlong(Vector3 from, float radius)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (!c.Exposed || (c.State != Mode.Gawk && !c._gawkAt.HasValue)) continue;
                if ((c.Tf.position - from).sqrMagnitude > radius * radius) continue;
                c._gawkAt = null;
                c._threat = from;
                c._fear = Mathf.Max(c._fear, 0.25f);
                if (c.State == Mode.Gawk)
                {
                    c.State = Mode.Walking;
                    c.Hurry(15f);
                    c.SetPose(PoseWalk);
                }
            }
        }

        /// <summary>The overlay's word for what he is doing.</summary>
        protected override string TraceState() => State.ToString();

        /// <summary>Sat on a bench, stood at a window, waiting at a kerb: not stuck.</summary>
        protected override bool Moving
            => base.Moving && (State == Mode.Walking || State == Mode.Flee || State == Mode.ToDoor ||
                               State == Mode.ToBench || State == Mode.FromBench);

        public string StatusLine => State switch
        {
            Mode.Flee => "Running from the shooting",
            Mode.Cower => "Down behind cover",
            Mode.Startle => "Startled",
            Mode.Gawk => "Staring at the scene",
            Mode.Dead => "Down",
            _ => "About their business",
        };
    }
}
