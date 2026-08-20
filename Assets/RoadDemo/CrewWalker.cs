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

        public CrewWalker() { Tag = "crew"; }

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
            _coverSpot = null;
            InCover = false;
            _returnTo = null;
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
            _coverSpot = null;
            InCover = false;
            EndChat();
            _hold = delay;
            point.y = Tf.position.y;
            _legs.Clear();
            _legTo = point;
            BeginLeg();
            State = Mode.Striding;
        }

        // the corners of a way across the city, and which one he is walking at
        readonly List<Vector3> _legs = new List<Vector3>();
        int _legAt, _replans;
        Vector3 _legEnd;

        /// <summary>Be there, and never mind the pavements.
        ///
        /// The crowd keeps to the sidewalk graph and waits at its lights because that
        /// is what a city looks like. The outfit does not: told to be somewhere, a man
        /// cuts over the lot, across the road against the light, down the gap between
        /// two buildings. The one thing he cannot do is walk through a wall, so the way
        /// is drawn round the walls first (WalkRoute) and he walks its corners; the
        /// cars and the crowd he steers past as he goes, like any other stride.
        ///
        /// No way at all - walled in, or a mark stood inside something - and he simply
        /// walks at it and gets as near as the ground lets him.</summary>
        public void OrderAcross(Vector3 point, float delay = 0f)
        {
            if (Dead) return;
            Target = null;
            _coverSpot = null;
            InCover = false;
            EndChat();
            _hold = delay;
            point.y = Tf.position.y;
            _legEnd = point;
            _replans = 0;
            if (!WalkRoute.Plan(Tf.position, point, _legs)) _legs.Clear();
            _legAt = 0;
            _legTo = _legs.Count > 0 ? _legs[0] : point;
            var far = point - Tf.position;
            far.y = 0f;
            _acrossBest = far.magnitude;
            _acrossFor = 0f;
            if (DriveTrace.On)
                DriveTrace.Event("walk", DisplayName, _legs.Count > 0
                    ? $"a way across: {_legs.Count} corners, {_acrossBest:F0} m"
                    : $"NO WAY across the {_acrossBest:F0} m - walking straight at it");
            BeginLeg();
            State = Mode.Striding;
        }

        // How near the far end he has ever been on this order, and how long since he
        // was nearer. A leg of its own can be walked perfectly while the WALK gets
        // nowhere - a man who reaches a corner, is turned back by something the map
        // does not know about, reaches it again, and paces that metre until the scene
        // closes. Getting there is measured against the far end, not the next corner.
        float _acrossBest, _acrossFor;

        /// <summary>The next corner, when there is one. Reached one, he goes on to the
        /// next; STOPPED SHORT of one, the way is drawn again from where he stands -
        /// it was drawn before he set off and the street has moved since. A few of
        /// those in a row and he is genuinely walled in, and he stands.</summary>
        bool NextLeg(bool arrived)
        {
            if (arrived)
            {
                _replans = 0;
                if (++_legAt < _legs.Count) { _legTo = _legs[_legAt]; BeginLeg(); return true; }
                _legs.Clear();
                return false;
            }
            if (_legs.Count == 0 || _replans >= 3) { _legs.Clear(); return false; }
            _replans++;
            if (!WalkRoute.Plan(Tf.position, _legEnd, _legs) || _legs.Count == 0)
            { _legs.Clear(); return false; }
            _legAt = 0;
            _legTo = _legs[0];
            BeginLeg();
            return true;
        }

        /// <summary>Close on this man and shoot him. Nothing happens unarmed.</summary>
        public void Engage(CrewWalker target)
        {
            if (Dead || Riding || !Armed || Panicked || target == null || target.Dead || target == this) return;
            if (Target != target) { _coverLooked = false; _underFire = 0; }
            Target = target;
            EndChat();
            _blockedFor = 0f;
            _steerSide = 0;
            _strideDir = Vector3.zero;
            State = Mode.Engaging;
            if (_fireTimer <= 0f)
                _fireTimer = Ballistics.Interval * Random.Range(0.4f, 1f); // squares up first
        }

        /// <summary>Lower the gun and stand.</summary>
        public void Disengage()
        {
            if (Dead) return;
            Target = null;
            _coverSpot = null;
            InCover = false;
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
            if (DriveTrace.On) TracePed(dt);
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
                    // ran and got his nerve back: back to where he stood
                    if (_shaken <= 0f && _returnTo.HasValue && Target == null)
                    {
                        var back = _returnTo.Value;
                        _returnTo = null;
                        OrderToPoint(back, Random.Range(0.2f, 1f));
                        return;
                    }
                    if (_shoutLeft > 0f) { TickShout(dt); return; }
                    if (Alert) { TickAlert(dt); return; }
                    TickLoiter(dt);
                    return;

                case Mode.Striding:
                {
                    if (HoldingBeat(dt)) return;
                    TickStride(dt, _legTo, 0.15f);
                    var gap = _legTo - Tf.position;
                    gap.y = 0f;
                    float left = gap.magnitude;
                    // there, or as near as the street lets him: a spot another man is
                    // stood on, or a car is parked on, is not reached, it is stopped
                    // short of - no marching in place. A corner on the way somewhere
                    // else is not a spot to stand on either: near enough IS round it.
                    bool last = _legAt >= _legs.Count - 1;
                    // A CORNER IS ROUNDED CLOSELY. Counting it reached from a stride and a
                    // half away starts the next line up to that much off the corner, and
                    // the line to the corner after it was drawn from the corner itself -
                    // so it can clip the very wall the corner was there to get round.
                    bool there = left <= (last ? 0.15f : 0.5f);

                    // pacing a metre back and forth is not walking anywhere
                    if (!there && _legs.Count > 0)
                    {
                        var toEnd = _legEnd - Tf.position;
                        toEnd.y = 0f;
                        float end = toEnd.magnitude;
                        if (end < _acrossBest - 1f) { _acrossBest = end; _acrossFor = 0f; }
                        else if ((_acrossFor += dt) > 5f)
                        {
                            _acrossFor = 0f;
                            if (DriveTrace.On)
                            {
                                var want = _legTo - Tf.position;
                                want.y = 0f;
                                float ahead = WalkObstacles.Clear(Tf.position, want, WalkObstacles.Radius, 6f);
                                var sb = DriveTrace.Take();
                                DriveTrace.Str(sb, "who", DisplayName);
                                DriveTrace.Str(sb, "what", $"no nearer than {end:F0} m for five seconds");
                                DriveTrace.Vec(sb, "p", Tf.position);
                                DriveTrace.Vec(sb, "leg", _legTo);
                                DriveTrace.Int(sb, "corner", _legAt);
                                DriveTrace.Int(sb, "corners", _legs.Count);
                                DriveTrace.Num(sb, "clear", ahead);
                                DriveTrace.Bool(sb, "inside", WalkObstacles.Standing(Tf.position, WalkObstacles.Radius));
                                DriveTrace.Row("walk", sb.ToString());
                            }
                            if (NextLeg(false)) return;
                            State = Mode.Standing;
                            return;
                        }
                    }

                    if (!there && !LegStalled(left, dt)) return;
                    if (NextLeg(there)) return;
                    State = Mode.Standing;
                    return;
                }

                case Mode.Engaging:
                    TickEngage(dt);
                    return;

                case Mode.Riding:
                    // seated in the car (the arena carries him); gun out of the window
                    // when there is someone to shoot at, else just sitting.
                    //
                    // On a bike none of that applies: BikePose writes his arms, his legs
                    // and his spine every frame over whatever plays here, so what plays
                    // here only has to sit his pelvis down and keep him breathing - and
                    // it must never be the aim clip, which would fight the pose for the
                    // gun arm and lose in a different place every frame.
                    if (Astride)
                        SetPose(HasPose(PoseRide) ? PoseRide : HasPose(PoseSit) ? PoseSit : PoseIdle);
                    else
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
                    if (left <= 0.4f || LegStalled(left, dt))
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

        // ------------------------------------------------------------------ the stride

        /// <summary>Metres looked down the line for what is in the way - a car's
        /// length, near enough: he leans off it early and passes it in one curve.</summary>
        const float Lookahead = 3f;

        int _steerSide;      // which way round the last thing in his way he went (WalkObstacles)
        Vector3 _strideDir;  // the line he stepped along last frame; zero at the start of a leg
        float _blockedFor;   // seconds stood on this leg with nowhere to step
        bool _detouring;     // this frame's step was off the line to the spot, round something

        // The stride: at the point, turning as it goes - at a walk, at the hurried
        // walk when there is a fight to get to, or flat out when he is running from
        // one (the jog clip, if there is one). Not through anything: the line he
        // takes is the one the ground allows (WalkObstacles) - straight at the spot
        // while that is clear, else the nearest line off it round the car, the bin,
        // the wall, and straight again once he is past. A spot inside something (the
        // click landed on a car; a hood's place in the formation fell on a bench) is
        // walked straight at and stopped short of - there is no way round to it.
        void TickStride(float dt, Vector3 to, float stopWithin, bool hurry = false, bool run = false)
        {
            var delta = to - Tf.position;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist <= stopWithin)
            {
                Loco(dt, false);
                _blockedFor = 0f;
                _detouring = false;
                return;
            }
            bool jog = run && HasPose(PoseJog);
            float pace = jog ? JogSpeed : hurry ? Speed * HurryFactor : Speed;
            var want = delta / dist;

            Vector3 dir;
            float clear;
            if (WalkObstacles.Occupied(to, WalkObstacles.Radius))
            {
                dir = want;
                clear = WalkObstacles.Clear(Tf.position, want, WalkObstacles.Radius, dist);
            }
            else
                dir = WalkObstacles.Steer(Tf.position, want, _strideDir, WalkObstacles.Radius,
                    Mathf.Min(Lookahead, dist), ref _steerSide, out clear);
            _detouring = Vector3.Dot(dir, want) < 0.995f;

            float step = Mathf.Min(pace * dt, Mathf.Min(dist, clear));
            if (step > 1e-4f)
            {
                Tf.position += dir * step;
                _strideDir = dir;
                _blockedFor = 0f;
            }
            else _blockedFor += dt;

            // he turns to the line he is walking; boxed in, he at least faces the spot
            Tf.rotation = Quaternion.Slerp(Tf.rotation,
                Quaternion.LookRotation(step > 1e-4f ? dir : want), 8f * dt);

            bool moving = step > 1e-4f;
            if (jog && moving) { SetPose(PoseJog); TickBlend(dt); }
            else if (!moving) Loco(dt, false);
            else
            {
                Loco(dt, true);
                // the walk clip keeps step with the pace: quicker feet for the hurried walk
                SetPoseSpeed(PoseWalk, pace / ClipPace(PoseWalk, WalkClipPace));
            }
        }

        // ------------------------------------------------------------------ the leg

        // Has this leg come to its end short of the spot? A leg ends at the spot, or
        // as near as he can get: stood still with nowhere to step (boxed in by cars
        // and walls), walking straight at it and getting no nearer (another man is
        // stood on it; the crowd will not let him through), or round something for so
        // long that he has plainly lost it. While he is going round a thing and still
        // moving he is given his time: a car's flank takes a while to pass.
        float _bestLegDist = float.MaxValue, _stall, _wander;

        void BeginLeg()
        {
            _bestLegDist = float.MaxValue;
            _stall = 0f;
            _wander = 0f;
            _blockedFor = 0f;
            _steerSide = 0;
            _strideDir = Vector3.zero;
        }

        bool LegStalled(float left, float dt)
        {
            if (left < _bestLegDist - 0.03f)
            {
                _bestLegDist = left;
                _stall = 0f;
                _wander = 0f;
                return false;
            }
            _wander += dt;
            if (!_detouring || _blockedFor > 0f) _stall += dt;
            return _stall > 0.7f || _wander > 8f;
        }

        // ------------------------------------------------------------------ the car

        /// <summary>In a car seat, carried by the car; takes no orders of his own.</summary>
        public bool Riding => State == Mode.Riding;

        /// <summary>While riding: gun up and out of the window (the drive-by).</summary>
        public bool RidingAim;

        /// <summary>What he is shooting at out of the window (or nothing) - the arena's
        /// call while he rides; the seat, not the man, decides what he can see.</summary>
        public void AimAt(CrewWalker mark) => Target = mark != null && !mark.Dead ? mark : null;

        /// <summary>Astride something rather than sat in it - a motorcycle. His legs
        /// stay where everyone can see them and BikePose puts them on the pegs.</summary>
        public bool Astride { get; private set; }

        /// <summary>Put in a seat, or set down beside the car again.</summary>
        public void SetRiding(bool on) => SetRiding(on, astride: false);

        /// <summary>The same, saying which kind of seat it is. A car's seat folds his
        /// legs away under the sill; a saddle cannot - on a bike his legs ARE the pose,
        /// and folding them would leave a man riding side-saddle on his own stumps.</summary>
        public void SetRiding(bool on, bool astride)
        {
            Astride = on && astride;
            // the legs go with the seat either way - a dead man is lifted out whole
            HideLegs(on && !astride);
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

        // The pack's men are a size too big for the pack's cars: sat on the cushion
        // their shins come out under the sills. In a seat the legs are folded away -
        // the two thigh bones scaled to nothing, which takes the shins and feet with
        // them - and unfolded the moment he is set down outside. It happens on the
        // same frame he is moved into or out of the seat, so nothing shows.
        Transform[] _legBones;
        Vector3[] _legScales;
        bool _legsHidden;

        void HideLegs(bool hide)
        {
            if (hide == _legsHidden) return;
            if (_legBones == null)
            {
                var found = new List<Transform>();
                var animator = Tf ? Tf.GetComponentInChildren<Animator>() : null;
                if (animator != null && animator.avatar != null && animator.avatar.isHuman)
                {
                    var l = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                    var r = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    if (l) found.Add(l);
                    if (r) found.Add(r);
                }
                _legBones = found.ToArray();
                _legScales = new Vector3[_legBones.Length];
                for (int i = 0; i < _legBones.Length; i++) _legScales[i] = _legBones[i].localScale;
            }
            for (int i = 0; i < _legBones.Length; i++)
                if (_legBones[i]) _legBones[i].localScale = hide ? Vector3.one * 0.001f : _legScales[i];
            _legsHidden = hide;
        }

        // ------------------------------------------------------------------ nerve

        Vector3 _fleeTo;
        float _shaken;
        bool _nerveRolled;
        Vector3? _returnTo;
        float _alertUntil, _alertBeat;
        Vector3 _alertAt;
        float _shoutLeft;
        int _underFire;

        /// <summary>Heard shooting lately and stood ready for it - gun out, turned to
        /// where it came from - though not in a fight himself.</summary>
        public bool Alert => Time.time < _alertUntil;

        /// <summary>Running from the scene for good: the arena takes him off the street
        /// once he is out of sight.</summary>
        public bool Retreating { get; private set; }

        /// <summary>Behind a car's tin, firing over it - a harder man to hit.</summary>
        public bool InCover { get; private set; }

        Vector3? _coverSpot;

        /// <summary>The arena's answer to "where can this man duck behind, this near
        /// his target": a spot, or null. Set by whoever owns the cars.</summary>
        public static System.Func<CrewWalker, Vector3, Vector3?> FindCover;

        /// <summary>A shot went off within earshot: a man with nothing on draws and
        /// turns toward it - and stays that way a while after the last one. Nothing
        /// while he fights, runs or rides.</summary>
        public void HearShot(Vector3 where)
        {
            if (Dead || Riding) return;
            bool wasAlert = Alert;
            _alertAt = where;
            _alertUntil = Time.time + 12f;
            if (State == Mode.Standing)
            {
                if (_chatPartner != null) EndChat();
                // the beat of taking it in, only for the first shot he hears
                if (!wasAlert) _alertBeat = Random.Range(0.2f, 0.8f);
            }
        }

        // Ready: gun low in the hand, turned to where the shooting was, a look about now
        // and then; the beat before it is the man taking it in.
        void TickAlert(float dt)
        {
            if (_alertBeat > 0f)
            {
                _alertBeat -= dt;
                Loco(dt, false);
                return;
            }
            var to = _alertAt - Tf.position;
            to.y = 0f;
            _idleTimer -= dt;
            if (_idleTimer <= 0f)
            {
                _idleTimer = Random.Range(1.5f, 4f);
                _lookYaw = Random.value < 0.6f && to.sqrMagnitude > 1e-3f
                    ? Quaternion.LookRotation(to.normalized).eulerAngles.y + Random.Range(-25f, 25f)
                    : Tf.eulerAngles.y + Random.Range(-70f, 70f);
            }
            if (!float.IsNaN(_lookYaw))
            {
                var want = Quaternion.Euler(0f, _lookYaw, 0f);
                Tf.rotation = Quaternion.RotateTowards(Tf.rotation, want, 150f * dt);
                if (Quaternion.Angle(Tf.rotation, want) < 0.5f) _lookYaw = float.NaN;
            }
            SetPose(Armed && HasPose(PosePistolIdle) ? PosePistolIdle : PoseIdle);
            TickBlend(dt);
        }

        /// <summary>Stand and shout for this long (the officer's warning).</summary>
        public void Shout(float seconds)
        {
            if (Dead || Riding) return;
            _shoutLeft = seconds;
            if (HasPose(PoseShout)) RestartPose(PoseShout);
        }

        void TickShout(float dt)
        {
            _shoutLeft -= dt;
            var to = _alertAt - Tf.position;
            to.y = 0f;
            if (to.sqrMagnitude > 1e-3f)
                Tf.rotation = Quaternion.RotateTowards(Tf.rotation, Quaternion.LookRotation(to.normalized), 200f * dt);
            SetPose(HasPose(PoseShout) ? PoseShout : Armed && HasPose(PosePistolIdle) ? PosePistolIdle : PoseIdle);
            TickBlend(dt);
        }

        /// <summary>Off the scene for good: a long run away from here, and gone.</summary>
        public void Retreat(Vector3 from)
        {
            if (Dead) return;
            Retreating = true;
            _returnTo = null;
            Flee(from, 60f, 90f, comeBack: false);
        }

        /// <summary>A round came close (or landed): enough of them and he looks for
        /// something to get behind.</summary>
        public void UnderFire() => _underFire++;

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

        public void Flee(Vector3 from) => Flee(from, 18f, 28f, comeBack: false);

        /// <summary>Run from here, <paramref name="near"/>-<paramref name="far"/> metres;
        /// with <paramref name="comeBack"/> he walks back to where he stood once his
        /// nerve returns (a man rattled by a friend going down beside him).</summary>
        public void Flee(Vector3 from, float near, float far, bool comeBack)
        {
            if (Dead) return;
            Target = null;
            _coverSpot = null;
            InCover = false;
            if (comeBack && !_returnTo.HasValue) _returnTo = Tf.position;
            EndChat();
            var away = Tf.position - from;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-3f) away = -Tf.forward;
            away.Normalize();
            // not straight back: a little off the line, so two men do not run one road
            // - and not into a wall or a car: a few rolls for a spot he can stand on,
            // then whatever the last roll gave (he stops short of it, as near as he gets)
            for (int roll = 0; roll < 6; roll++)
            {
                var line = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f) * away;
                _fleeTo = Tf.position + line * Random.Range(near, far);
                if (!WalkObstacles.Occupied(_fleeTo, WalkObstacles.Radius)) break;
            }
            BeginLeg();
            // a beat of nerve failing before the legs go, and the run picked up at a
            // random stride, at a rate of his own - not the crew's one run, in step
            _hold = Random.Range(0.1f, 0.6f);
            ScatterPhase(PoseJog);
            SetPoseSpeed(PoseJog, _runRate * Random.Range(0.94f, 1.06f));
            State = Mode.Fleeing;
        }

        void TickEngage(float dt)
        {
            // A man he was shooting at whose BODY has since been taken off the street
            // (the gore cleans up, a rival that broke and ran is despawned) is not null
            // the way a C# reference is null - his Tf is a destroyed Unity object, and
            // reading it throws. It threw every frame for every engaged man, which is
            // to say the fight stopped dead and nobody fired another round.
            if (Target == null || !Target.Tf || Target.Dead || !Armed)
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
            // pressed - one from the ground, or rounds landing all round him - and
            // something to get behind close by: he goes behind it and fires from there
            if (!_coverSpot.HasValue && !_coverLooked && (Health <= 1 || _underFire >= 3) && FindCover != null)
            {
                _coverLooked = true;
                _coverSpot = FindCover(this, Target.Tf.position);
            }
            if (_coverSpot.HasValue)
            {
                var spot = _coverSpot.Value;
                var gap = spot - Tf.position;
                gap.y = 0f;
                if (dist > range * 1.3f) { _coverSpot = null; InCover = false; } // out of reach from here: leave it
                else if (gap.magnitude > 0.5f)
                {
                    InCover = false;
                    TickStride(dt, spot, 0.4f, hurry: true);
                    // no way through to it (the car has rolled on, something else
                    // stands in the way): he fights from where he is instead
                    if (_blockedFor > 0.8f) { _coverSpot = null; _blockedFor = 0f; }
                    return;
                }
                else InCover = true;
            }

            bool closing = !_coverSpot.HasValue && (_wasClosing ? dist > range * RangeFactor : dist > range * 1.15f);
            _wasClosing = closing;
            if (closing)
            {
                // He does not walk into a fight with the gun down. Inside his reach he
                // fires as he comes on - slower and worse than a man stood squared up,
                // but the alternative is what the lab watched three times over: a crew
                // marching up to a mob that was already shooting, taking the whole
                // exchange without answering a round of it, and going down to the last man.
                _fireTimer -= dt;
                if (dist <= range && _fireTimer <= 0f && Vector3.Angle(Tf.forward, toTarget) < 40f)
                {
                    _fireTimer = Ballistics.Interval * OnTheMove;
                    if (HasPose(PoseShoot))
                    {
                        RestartPose(PoseShoot);
                        _shootHold = Mathf.Min(PoseLength(PoseShoot), 0.45f);
                    }
                    Fired?.Invoke(this);
                }
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

        /// <summary>How much slower a man shoots while he is still closing.</summary>
        const float OnTheMove = 1.5f;

        bool _wasClosing, _coverLooked;
        bool _gunDropped;

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
        public bool Loitering => State == Mode.Standing && _chatPartner == null && ChatCooldown <= 0f && !Alert && _shoutLeft <= 0f && !Retreating;

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

        /// <summary>Put back on the graph where he actually stands - set down out of a
        /// car at a kerb streets away from the stretch he boarded on, or walked off his
        /// stretch to a door. His link and metre are changed to the nearest sidewalk
        /// the arena found; his feet stay where they are, the sideways gap kept as his
        /// lateral so the first step of the next order is a step, not a jump. Off the
        /// graph (the free floor) nothing changes.</summary>
        public void Reseat(PedLink link, float t)
        {
            if (_link == null || link == null || link.Length <= 0.01f || Dead || Riding) return;
            _link = link;
            _t = Mathf.Clamp(t, 0f, link.Length);
            _cameFrom = link.From;
            Waiting = false;
            _route = null;
            _destFwd = _destBack = null;
            var ab = link.To.Pos - link.From.Pos;
            ab.y = 0f;
            if (ab.sqrMagnitude > 1e-4f && Tf)
            {
                var dirN = ab.normalized;
                var right = new Vector3(dirN.z, 0f, -dirN.x);
                var q = Vector3.Lerp(link.From.Pos, link.To.Pos, _t / link.Length);
                var off = Tf.position - q;
                off.y = 0f;
                Lateral = Mathf.Clamp(Vector3.Dot(off, right), -1.9f, 1.9f);
            }
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
        protected override string TraceState()
            => State + (Target != null ? " at " + Target.DisplayName : "");

        /// <summary>A crew man is going somewhere only under an order; stood on his
        /// corner with his hands in his pockets, he is where he means to be.</summary>
        protected override bool Moving => HasOrder;

        public string StatusLine => State switch
        {
            Mode.Standing => Retreating ? "Gone" : Alert ? "On alert - shots heard" : "Standing by",
            Mode.Walking => "On the move, heading " + PatrolInfo.Heading(Tf),
            Mode.Striding => "On the move, heading " + PatrolInfo.Heading(Tf),
            Mode.Homing => "Almost there",
            Mode.Engaging => Target != null ? (InCover ? "Behind cover, shooting at " : "Shooting at ") + Target.DisplayName : "Engaging",
            Mode.Fleeing => Retreating ? "Getting out of here" : "Running for it",
            Mode.Riding => "In the car",
            Mode.Dead => "Down",
            _ => string.Empty,
        };
    }
}
