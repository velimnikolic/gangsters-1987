using System.Collections.Generic;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    // One man of the outfit on the demo's streets: a lieutenant or one of his hoods
    // - or a rival's. Unlike the civilians he does not wander - he stands where he
    // was left until an order sends him somewhere: over the sidewalk graph in the
    // city (same crossings, same lights as the crowd, routed by shortest metres),
    // or in a straight stride across the empty demo floor. Armed, he carries the
    // ledger's gun in his right hand; engaged, he closes to his gun's range,
    // squares up, and fires on his cadence until the other man is down or he is.
    // Who is hit is not his call - he raises the gun and reports the shot; the
    // arena (DemoCrews) rolls the dice and hands out the wounds.
    public class CrewWalker : PedestrianAgent
    {
        public enum Mode { Standing, Walking, Homing, Striding, Engaging, Fleeing, Riding, Dead }

        public Mode State { get; private set; } = Mode.Standing;

        /// <summary>The roster id of the man this figure stands for (rivals carry
        /// their own negative ids - they are on nobody's books).</summary>
        public int CharacterId;
        public string DisplayName = "";
        public bool IsLieutenant;

        /// <summary>The pack prefab this body was cast from - the ledger recasts a man
        /// on promotion (a lieutenant wears a suit), and the street must follow.</summary>
        public GameObject SourcePrefab;

        /// <summary>0 is the outfit; anything else is a rival mob.</summary>
        public int Faction;

        // ------------------------------------------------------------------ arms

        public Transform Weapon { get; private set; }
        public EquipmentKind WeaponKind { get; private set; }
        public GameObject WeaponPrefab { get; private set; }
        public bool Armed => Weapon != null;
        public CrewArms.Stats Ballistics { get; private set; }

        public int Health = 3;
        public int MaxHealth = 3;
        public bool Dead => State == Mode.Dead;

        /// <summary>Whom he is shooting at, or null.</summary>
        public CrewWalker Target { get; private set; }

        /// <summary>The last man who put a bullet in him - the arena's cue to answer.</summary>
        public CrewWalker LastAttacker { get; private set; }

        /// <summary>Raised the frame a shot leaves the barrel. The arena resolves it.</summary>
        public System.Action<CrewWalker> Fired;

        float _fireTimer;
        float _shootHold;
        float _flinch;

        /// <summary>The sidewalk stretch this walker is on right now (may be a
        /// crossing mid-route) and how far along it - where a new hood is dealt in.
        /// Null off the graph (the free-floor demo).</summary>
        public PedLink CurrentLink => _link;
        public float CurrentT => _t;

        PedLink _destFwd, _destBack;
        float _destT;      // along _destFwd
        float _targetT;    // along whichever direction the walker stepped onto
        Dictionary<PedNode, PedLink> _route;
        Vector3 _legTo;    // the free stride's end

        public bool HasOrder => State == Mode.Walking || State == Mode.Homing || State == Mode.Striding || State == Mode.Fleeing;
        public bool OnGraph => _link != null;

        /// <summary>Where the current order ends - a point on the ordered sidewalk,
        /// or the stride's end.</summary>
        public Vector3 Destination
        {
            get
            {
                if (State == Mode.Striding) return _legTo;
                if (_destFwd == null) return Tf.position;
                return Vector3.Lerp(_destFwd.From.Pos, _destFwd.To.Pos, _destT / _destFwd.Length);
            }
        }

        public PedLink DestinationLink => _destFwd;
        public float DestinationT => _destT;

        // ------------------------------------------------------------------ orders

        /// <summary>Send the walker to metre <paramref name="t"/> along
        /// <paramref name="link"/> (either direction of a stretch is fine), after
        /// <paramref name="delay"/> seconds stood - a hood falling in a beat behind
        /// his boss rather than in the same frame.</summary>
        public void OrderTo(PedLink link, float t, float delay = 0f)
        {
            if (Dead || Riding || link == null || link.Length <= 0.01f || _link == null) return;
            Target = null;
            EndChat();
            _hold = delay;
            var back = Reverse(link);
            _destFwd = link;
            _destBack = back;
            _destT = Mathf.Clamp(t, 0.3f, link.Length - 0.3f);
            _route = RouteToward(link.From, link.To, out var dist);

            // already on the ordered stretch: just walk to the point
            if (StepOntoIfOn(_link, _t)) return;

            State = Mode.Walking;

            if (Waiting)
            {
                // stood at a light on a crossing (t = 0 at _link.From)
                if (!StepOntoAt(_link.From)) Reroute(_link.From);
                return;
            }

            // mid-link: carry on to the far node, or turn round to the near one,
            // whichever is the shorter road in metres
            float onward = float.MaxValue, backward = float.MaxValue;
            if (dist.TryGetValue(_link.To, out float dTo)) onward = (_link.Length - _t) + dTo;
            var rev = Reverse(_link);
            if (rev != null && dist.TryGetValue(_link.From, out float dFrom)) backward = _t + dFrom;
            if (backward < onward && rev != null)
            {
                _t = _link.Length - _t;
                _link = rev;
                _cameFrom = rev.From;
            }
        }

        /// <summary>Walk straight to this point over open ground - the empty floor's
        /// order. Off the graph only; on it the sidewalks are the way.</summary>
        public void OrderToPoint(Vector3 point, float delay = 0f)
        {
            if (Dead) return;
            Target = null;
            EndChat();
            _hold = delay;
            point.y = Tf.position.y;
            _legTo = point;
            _bestLegDist = float.MaxValue;
            _stall = 0f;
            State = Mode.Striding;
        }

        /// <summary>Close on this man and shoot him. Nothing happens unarmed.</summary>
        public void Engage(CrewWalker target)
        {
            if (Dead || Riding || !Armed || Panicked || target == null || target.Dead || target == this) return;
            Target = target;
            EndChat();
            State = Mode.Engaging;
            if (_fireTimer <= 0f)
                _fireTimer = Ballistics.Interval * Random.Range(0.4f, 1f); // squares up first
        }

        /// <summary>Lower the gun and stand.</summary>
        public void Disengage()
        {
            if (Dead) return;
            Target = null;
            if (State == Mode.Engaging) State = Mode.Standing;
        }

        // ------------------------------------------------------------------ arms

        /// <summary>Put this gun in his hand (replacing whatever he held); null disarms.</summary>
        public void Arm(GameObject prefab, EquipmentKind kind)
        {
            if (Weapon != null)
            {
                Object.Destroy(Weapon.gameObject);
                Weapon = null;
            }
            WeaponPrefab = prefab;
            WeaponKind = kind;
            Ballistics = CrewArms.StatsFor(kind);
            if (prefab == null) return;
            var animator = Tf.GetComponentInChildren<Animator>();
            Weapon = CrewArms.Attach(animator, prefab);
        }

        public Vector3 MuzzlePosition
        {
            get
            {
                var m = CrewArms.MuzzleOf(Weapon);
                return m ? m.position : Tf.position + Vector3.up * 1.4f;
            }
        }

        public Vector3 MuzzleForward
        {
            get
            {
                var m = CrewArms.MuzzleOf(Weapon);
                return m ? m.forward : Tf.forward;
            }
        }

        /// <summary>Chest height - where the other man aims.</summary>
        public Vector3 ChestPosition => Tf.position + Vector3.up * 1.3f;

        /// <summary>A bullet landed. Enough of them and he goes down.</summary>
        public void TakeHit(int damage, CrewWalker from)
        {
            if (Dead) return;
            LastAttacker = from;
            Health -= Mathf.Max(1, damage);
            if (Health <= 0)
            {
                Kill();
                return;
            }
            if (HasPose(PoseHit))
            {
                RestartPose(PoseHit, 0f, Random.Range(0.9f, 1.2f));
                _flinch = Mathf.Min(PoseLength(PoseHit), 0.9f) * 0.8f;
            }
        }

        public void Kill()
        {
            if (Dead) return;
            Health = 0;
            Target = null;
            EndChat();
            State = Mode.Dead;
            if (HasPose(PoseDeath))
            {
                // no two falls at quite the same pace, and a half-turn of stagger
                // before it - the same clip twice in a row does not read as the same clip
                RestartPose(PoseDeath, 0f, Random.Range(0.8f, 1.15f));
                Tf.rotation *= Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f);
                SetPose(PoseDeath);
            }
            else
            {
                // no fall clip: lie him down the crude way
                Tf.rotation *= Quaternion.Euler(-90f, 0f, 0f);
                SetPose(PoseIdle);
            }
        }

        // ------------------------------------------------------------------ frame

        public void TickCrew(float dt)
        {
            switch (State)
            {
                case Mode.Dead:
                    TickBlend(dt);
                    if (HasPose(PoseDeath))
                    {
                        float len = PoseLength(PoseDeath), at = PoseTime(PoseDeath);
                        // the gun leaves the hand part-way down and lies where it fell
                        if (!_gunDropped && at >= len * 0.45f) DropGun();
                        if (at >= len - 0.03f) HoldPose(PoseDeath);
                    }
                    else if (!_gunDropped) DropGun();
                    return;

                case Mode.Standing:
                    if (_shaken > 0f) _shaken -= dt;
                    TickLoiter(dt);
                    return;

                case Mode.Striding:
                {
                    if (HoldingBeat(dt)) return;
                    TickStride(dt, _legTo, 0.15f);
                    var gap = _legTo - Tf.position;
                    gap.y = 0f;
                    float left = gap.magnitude;
                    // there, or as near as the crowd lets him: a spot another man is
                    // stood on is not reached, it is stopped short of - no marching in place
                    if (left < _bestLegDist - 0.03f) { _bestLegDist = left; _stall = 0f; }
                    else _stall += dt;
                    if (left <= 0.15f || _stall > 0.7f)
                        State = Mode.Standing;
                    return;
                }

                case Mode.Engaging:
                    TickEngage(dt);
                    return;

                case Mode.Riding:
                    // seated in the car (the arena carries him); gun out of the window
                    // when there is someone to shoot at, else just sitting
                    SetPose(RidingAim && HasPose(PoseAim) ? PoseAim : HasPose(PoseSit) ? PoseSit : PoseIdle);
                    TickBlend(dt);
                    return;

                case Mode.Fleeing:
                {
                    // the beat before he bolts: the flinch finishes, or he just stands a
                    // moment - no two men break at the same instant
                    if (_hold > 0f)
                    {
                        _hold -= dt;
                        if (_flinch > 0f) { _flinch -= dt; SetPose(PoseHit); }
                        else SetPose(PoseIdle);
                        TickBlend(dt);
                        return;
                    }
                    TickStride(dt, _fleeTo, 0.4f, run: true);
                    var gap = _fleeTo - Tf.position;
                    gap.y = 0f;
                    float left = gap.magnitude;
                    if (left < _bestLegDist - 0.03f) { _bestLegDist = left; _stall = 0f; }
                    else _stall += dt;
                    if (left <= 0.4f || _stall > 0.7f)
                    {
                        State = Mode.Standing;
                        _shaken = Random.Range(8f, 14f); // out of it a while, then game again
                    }
                    return;
                }

                default: // Walking, Homing - the graph
                    if (HoldingBeat(dt)) return;
                    Tick(dt);
                    if (State == Mode.Homing && _t >= _targetT)
                        State = Mode.Standing;
                    return;
            }
        }

        // ------------------------------------------------------------------ the beat

        // Seconds an order waits before the legs move: a hood does not set off the
        // frame his boss does, and a man does not bolt the frame he is hit. Held,
        // he stands (or finishes his flinch); then he goes.
        float _hold;

        /// <summary>Spends the beat, standing; true while there is beat left.</summary>
        bool HoldingBeat(float dt)
        {
            if (_hold <= 0f) return false;
            _hold -= dt;
            Loco(dt, false);
            return true;
        }

        /// <summary>Metres a second at the run - the pace a man gets away at. Dealt per
        /// man (SetJog) so a crew does not run in step; a man dealt the sprint clip
        /// runs quicker than one dealt the jog, since his feet say so.</summary>
        public float JogSpeed = 3.1f;

        /// <summary>The library's jog covers about this much ground a second at speed 1
        /// - the figure used only when the clip itself does not say.</summary>
        const float JogClipPace = 3.0f;

        /// <summary>How far off its natural rate a run clip may be played before the
        /// feet read wrong - a sprint at half speed is a moon-walk, so a man asked to
        /// jog on a sprint clip runs a real sprint instead.</summary>
        const float RunRateMin = 0.85f, RunRateMax = 1.25f;

        float _runRate = 1f;

        public void SetJog(float speed)
        {
            float natural = ClipPace(PoseJog, JogClipPace);
            _runRate = Mathf.Clamp(speed / natural, RunRateMin, RunRateMax);
            JogSpeed = _runRate * natural;
            SetPoseSpeed(PoseJog, _runRate);
        }

        /// <summary>How much quicker a man walks when he is closing on a fight - a
        /// hurried walk, not a run; running is for getting away.</summary>
        const float HurryFactor = 1.3f;

        // The stride: straight at the point, turning as it goes - at a walk, at the
        // hurried walk when there is a fight to get to, or flat out when he is
        // running from one (the jog clip, if there is one).
        void TickStride(float dt, Vector3 to, float stopWithin, bool hurry = false, bool run = false)
        {
            var delta = to - Tf.position;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist <= stopWithin)
            {
                Loco(dt, false);
                return;
            }
            bool jog = run && HasPose(PoseJog);
            float pace = jog ? JogSpeed : hurry ? Speed * HurryFactor : Speed;
            var dir = delta / dist;
            Tf.rotation = Quaternion.Slerp(Tf.rotation, Quaternion.LookRotation(dir), 8f * dt);
            Tf.position += dir * Mathf.Min(pace * dt, dist);
            if (jog) { SetPose(PoseJog); TickBlend(dt); }
            else
            {
                Loco(dt, true);
                // the walk clip keeps step with the pace: quicker feet for the hurried walk
                SetPoseSpeed(PoseWalk, pace / ClipPace(PoseWalk, WalkClipPace));
            }
        }

        // ------------------------------------------------------------------ the car

        /// <summary>In a car seat, carried by the car; takes no orders of his own.</summary>
        public bool Riding => State == Mode.Riding;

        /// <summary>While riding: gun up and out of the window (the drive-by).</summary>
        public bool RidingAim;

        /// <summary>What he is shooting at out of the window (or nothing) - the arena's
        /// call while he rides; the seat, not the man, decides what he can see.</summary>
        public void AimAt(CrewWalker mark) => Target = mark != null && !mark.Dead ? mark : null;

        /// <summary>Put in a seat, or set down beside the car again.</summary>
        public void SetRiding(bool on)
        {
            if (Dead) return;
            if (on)
            {
                Target = null;
                EndChat();
                State = Mode.Riding;
            }
            else if (State == Mode.Riding)
            {
                RidingAim = false;
                State = Mode.Standing;
            }
        }

        // ------------------------------------------------------------------ nerve

        Vector3 _fleeTo;
        float _shaken;
        bool _nerveRolled;

        /// <summary>Running from the fight, or too shaken to be sent back into it yet -
        /// the arena leaves such a man alone.</summary>
        public bool Panicked => State == Mode.Fleeing || _shaken > 0f;

        /// <summary>A hit that leaves him one from the ground may break him: not
        /// every man - the roll is made once - but the one it breaks drops the
        /// fight and runs from whoever hit him, and stays out of it a while after.</summary>
        public void MaybePanic(CrewWalker threat, float chance)
        {
            if (Dead || _nerveRolled || Health > 1) return;
            _nerveRolled = true;
            if (Random.value >= chance) return;
            Flee(threat != null && threat.Tf ? threat.Tf.position : Tf.position - Tf.forward);
        }

        public void Flee(Vector3 from)
        {
            if (Dead) return;
            Target = null;
            EndChat();
            var away = Tf.position - from;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-3f) away = -Tf.forward;
            away.Normalize();
            // not straight back: a little off the line, so two men do not run one road
            away = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f) * away;
            _fleeTo = Tf.position + away * Random.Range(18f, 28f);
            _bestLegDist = float.MaxValue;
            _stall = 0f;
            // a beat of nerve failing before the legs go, and the run picked up at a
            // random stride, at a rate of his own - not the crew's one run, in step
            _hold = Random.Range(0.1f, 0.6f);
            ScatterPhase(PoseJog);
            SetPoseSpeed(PoseJog, _runRate * Random.Range(0.94f, 1.06f));
            State = Mode.Fleeing;
        }

        void TickEngage(float dt)
        {
            if (Target == null || Target.Dead || !Armed)
            {
                Target = null;
                State = Mode.Standing;
                Loco(dt, false);
                return;
            }

            var toTarget = Target.Tf.position - Tf.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            float range = Ballistics.Range;

            // out of range: close in (to a little inside it, so a man who backs off
            // a step does not restart the walk); in range: square up and shoot
            // hysteresis both ways: he starts closing only once the man is well out of
            // range, and stops only once well inside it - no jogging in place at the line
            bool closing = _wasClosing ? dist > range * RangeFactor : dist > range * 1.15f;
            _wasClosing = closing;
            if (closing)
            {
                TickStride(dt, Target.Tf.position, range * RangeFactor, hurry: true); // a quick walk, no running
                return;
            }

            if (dist > 1e-3f)
                Tf.rotation = Quaternion.RotateTowards(Tf.rotation,
                    Quaternion.LookRotation(toTarget / dist), 360f * dt);

            if (_flinch > 0f)
            {
                _flinch -= dt;
                SetPose(PoseHit);
                TickBlend(dt);
                return;
            }

            _fireTimer -= dt;
            if (_shootHold > 0f)
            {
                _shootHold -= dt;
                SetPose(HasPose(PoseShoot) ? PoseShoot : PoseAim);
            }
            else
                SetPose(HasPose(PoseAim) ? PoseAim : PosePistolIdle);
            TickBlend(dt);

            // shoot only once squared up - a man firing over his shoulder reads wrong
            float off = Vector3.Angle(Tf.forward, toTarget);
            if (_fireTimer <= 0f && off < 25f)
            {
                _fireTimer = Ballistics.Interval;
                if (HasPose(PoseShoot))
                {
                    RestartPose(PoseShoot);
                    _shootHold = Mathf.Min(PoseLength(PoseShoot), 0.45f);
                }
                Fired?.Invoke(this);
            }
        }

        bool _wasClosing;
        bool _gunDropped;
        float _bestLegDist = float.MaxValue, _stall;

        /// <summary>How far inside his gun's range this man closes to before he stops
        /// and fires - dealt per man, so a crew fans out into a loose line instead of
        /// piling onto one point.</summary>
        public float RangeFactor = 0.8f;

        // The gun slips out of the fist and settles flat on the ground beside him,
        // barrel where the hand was pointing. It stays a child of nothing: a prop now.
        void DropGun()
        {
            _gunDropped = true;
            if (Weapon == null) return;
            var gun = Weapon;
            var muzzle = CrewArms.MuzzleOf(gun);
            var along = muzzle != null ? muzzle.forward : gun.forward;
            along.y = 0f;
            if (along.sqrMagnitude < 1e-4f) along = Tf.forward;
            var spot = gun.position;
            spot.y = Tf.position.y + 0.02f;
            gun.SetParent(null, true);
            gun.position = spot;
            // flat on its side: the barrel along the ground, the top turned to one flank
            var frame = Quaternion.LookRotation(along.normalized, Vector3.up) *
                        Quaternion.Euler(0f, 0f, Random.value < 0.5f ? 90f : -90f);
            // the muzzle marker carries the gun's own barrel/top frame; undo it so the
            // pose above is stated in that frame whatever axes the mesh was authored on
            gun.rotation = muzzle != null && muzzle != gun
                ? frame * Quaternion.Inverse(muzzle.localRotation)
                : frame;
            Weapon = null;
        }

        // The walk/stand crossfade. A man at ease stands at ease - the gun hangs at
        // his side in the plain idle; the ready stance is for a fight, not a corner.
        void Loco(float dt, bool walking)
        {
            SetPose(walking ? PoseWalk : PoseIdle);
            TickBlend(dt);
        }

        // ------------------------------------------------------------------ at ease

        float _idleTimer = 2f;
        float _lookYaw = float.NaN;
        CrewWalker _chatPartner;
        float _chatLeft, _turnLeft;
        bool _speaking;
        public float ChatCooldown = 6f;

        public bool Chatting => _chatPartner != null;

        /// <summary>Stood with nothing to do, free to be drawn into a word.</summary>
        public bool Loitering => State == Mode.Standing && _chatPartner == null && ChatCooldown <= 0f;

        /// <summary>Two men stop for a word: face each other, one talks, the other
        /// listens, and the floor changes hands every few seconds.</summary>
        public void BeginChat(CrewWalker partner, float seconds, bool speaksFirst)
        {
            if (Dead || State != Mode.Standing) return;
            _chatPartner = partner;
            _chatLeft = seconds;
            _speaking = speaksFirst;
            _turnLeft = Random.Range(2.5f, 4.5f);
            _lookYaw = float.NaN;
        }

        public void EndChat()
        {
            if (_chatPartner == null) return;
            _chatPartner = null;
            ChatCooldown = Random.Range(8f, 20f);
            _idleTimer = Random.Range(2f, 5f);
        }

        // Standing around like a person: a look this way and that now and then, or a
        // word with the man beside him. Never the pistol stance - a crew on a corner
        // is a crew on a corner until somebody gives it a reason.
        void TickLoiter(float dt)
        {
            ChatCooldown -= dt;

            if (_chatPartner != null)
            {
                if (_chatPartner.Dead || _chatPartner.State != Mode.Standing || _chatPartner._chatPartner != this)
                {
                    EndChat();
                }
                else
                {
                    _chatLeft -= dt;
                    _turnLeft -= dt;
                    if (_turnLeft <= 0f) { _speaking = !_speaking; _turnLeft = Random.Range(2.5f, 4.5f); }
                    var to = _chatPartner.Tf.position - Tf.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 1e-3f)
                        Tf.rotation = Quaternion.RotateTowards(Tf.rotation, Quaternion.LookRotation(to.normalized), 120f * dt);
                    SetPose(_speaking && HasPose(PoseTalk) ? PoseTalk : PoseIdle);
                    TickBlend(dt);
                    if (_chatLeft <= 0f) EndChat();
                    return;
                }
            }

            // a glance around: a new heading now and then, turned to at a stroll
            _idleTimer -= dt;
            if (_idleTimer <= 0f)
            {
                // on watch: a man on a corner keeps turning his head - a look this
                // way, a look that way, back to where he was
                _idleTimer = Random.Range(2f, 5.5f);
                if (Random.value < 0.7f)
                    _lookYaw = Tf.eulerAngles.y + Random.Range(-110f, 110f);
            }
            if (!float.IsNaN(_lookYaw))
            {
                var want = Quaternion.Euler(0f, _lookYaw, 0f);
                Tf.rotation = Quaternion.RotateTowards(Tf.rotation, want, 45f * dt);
                if (Quaternion.Angle(Tf.rotation, want) < 0.5f) _lookYaw = float.NaN;
            }
            Loco(dt, false);
        }

        // ------------------------------------------------------------------ the graph

        // Reaching either end of the ordered stretch steps onto it, aimed at the
        // point; anywhere else the routed choice below takes over.
        protected override bool OnArrived(PedNode node)
        {
            if (State == Mode.Homing)
            {
                // a long frame carried the walker past the point and off the far
                // node in one step - it is there, near enough
                State = Mode.Standing;
                return false;
            }
            if (State == Mode.Walking && StepOntoAt(node))
                return false;
            return true;
        }

        protected override PedLink ChooseLink(PedNode node, PedNode keepAwayFrom)
        {
            if (State == Mode.Walking && _route != null &&
                _route.TryGetValue(node, out var toward) && toward != null)
                return toward;
            return base.ChooseLink(node, keepAwayFrom);
        }

        bool StepOntoIfOn(PedLink link, float t)
        {
            if (link == _destFwd)
            {
                if (t <= _destT) { _targetT = _destT; State = Mode.Homing; }
                else Turn(_destBack, link.Length - t, link.Length - _destT);
                return true;
            }
            if (_destBack != null && link == _destBack)
            {
                float mirrored = link.Length - _destT;
                if (t <= mirrored) { _targetT = mirrored; State = Mode.Homing; }
                else Turn(_destFwd, link.Length - t, _destT);
                return true;
            }
            return false;
        }

        bool StepOntoAt(PedNode node)
        {
            if (node == _destFwd.From) { Turn(_destFwd, 0f, _destT); return true; }
            if (node == _destFwd.To && _destBack != null)
            {
                Turn(_destBack, 0f, _destFwd.Length - _destT);
                return true;
            }
            return false;
        }

        void Turn(PedLink link, float t, float targetT)
        {
            if (link == null) { State = Mode.Standing; return; }
            _link = link;
            _t = t;
            _cameFrom = link.From;
            _targetT = targetT;
            Waiting = false;
            State = Mode.Homing;
        }

        static PedLink Reverse(PedLink link)
        {
            foreach (var l in link.To.Links)
                if (l.To == link.From) return l;
            return null;
        }

        /// <summary>Dijkstra in metres from both ends of the target stretch over the
        /// (symmetric) ped graph, then the link toward the nearer neighbour per node.
        /// The graph is a few hundred nodes and orders are rare, so a plain scan for
        /// the next node stands in for a heap.</summary>
        static Dictionary<PedNode, PedLink> RouteToward(PedNode a, PedNode b,
            out Dictionary<PedNode, float> dist)
        {
            dist = new Dictionary<PedNode, float> { [a] = 0f, [b] = 0f };
            var open = new List<PedNode> { a, b };
            var closed = new HashSet<PedNode>();
            while (open.Count > 0)
            {
                int bestI = 0;
                float bestD = dist[open[0]];
                for (int i = 1; i < open.Count; i++)
                    if (dist[open[i]] < bestD) { bestD = dist[open[i]]; bestI = i; }
                var n = open[bestI];
                open.RemoveAt(bestI);
                if (!closed.Add(n)) continue;
                foreach (var l in n.Links)
                {
                    float d = bestD + l.Length;
                    if (dist.TryGetValue(l.To, out float known) && known <= d) continue;
                    dist[l.To] = d;
                    open.Add(l.To);
                }
            }

            var next = new Dictionary<PedNode, PedLink>();
            foreach (var kv in dist)
            {
                PedLink best = null;
                float bestD = float.MaxValue;
                foreach (var l in kv.Key.Links)
                    if (dist.TryGetValue(l.To, out float d) && d + l.Length < bestD)
                    {
                        bestD = d + l.Length;
                        best = l;
                    }
                if (best != null) next[kv.Key] = best;
            }
            return next;
        }

        /// <summary>The overlay's status line.</summary>
        public string StatusLine => State switch
        {
            Mode.Standing => "Standing by",
            Mode.Walking => "On the move, heading " + PatrolInfo.Heading(Tf),
            Mode.Striding => "On the move, heading " + PatrolInfo.Heading(Tf),
            Mode.Homing => "Almost there",
            Mode.Engaging => Target != null ? "Shooting at " + Target.DisplayName : "Engaging",
            Mode.Fleeing => "Running for it",
            Mode.Riding => "In the car",
            Mode.Dead => "Down",
            _ => string.Empty,
        };
    }
}
