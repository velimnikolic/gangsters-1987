using System.Collections.Generic;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    // One man of the outfit on the demo's streets: a lieutenant or one of his hoods
    // - or a rival's. He stands where he was left, and now and then he has had enough
    // of the corner and walks somewhere else in the town (TryRoam) - not the crowd's
    // wander, which is the sidewalk graph and its lights, but the outfit's: over a lot,
    // down the gap between two buildings, across the road. An order overrides it at any
    // moment: over the sidewalk graph in the city (same crossings, same lights as the
    // crowd, routed by shortest metres), or in a straight stride across the empty
    // demo floor. Armed, he carries the
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

        /// <summary>Stood at a crossing waiting on the light. The arena's tether reads
        /// it: a crew does not split over a red - a man his crew has already crossed
        /// away from cuts over after them instead of standing on the zebra alone.</summary>
        public bool AtLight => _link != null && Waiting;

        /// <summary>Deal this man his own line across the pavement. Every walker keeps
        /// right on much the same lane, so a crew walking one stretch walked it in
        /// single file; dealt lanes, the same walk reads as a pack abreast.</summary>
        public void HoldLane(float lane) => Lane = Mathf.Clamp(lane, -1.6f, 1.6f);

        /// <summary>Gear this man's walk (the arena's dawdle for a hood ahead of his
        /// crew). 1 is his own pace; a fraction is a slowed walk, still flowing -
        /// never a stop, which turns him into a bollard his own boss brakes behind.</summary>
        public void SetPace(float scale) => PaceScale = Mathf.Clamp(scale, 0.3f, 1.3f);

        /// <summary>Under the arena's hand right now - dawdled, lingered, or spending
        /// an ordered beat. The audit reads it: a man the tether is actively holding
        /// level with his crew is managed, not strayed, however wide the pavement
        /// between them while the boss threads his own lights.</summary>
        public bool ReinedIn => _hold > 0f || PaceScale < 0.99f;

        /// <summary>Quicker feet on the current stride - a hood strung out behind his
        /// crew catches it up rather than trailing it at his own pace. Cleared by the
        /// next order; the tether sets it again if he is still behind.</summary>
        public bool Hustle;

        /// <summary>Stand a beat where he is, mid-walk - the boss keeping step with
        /// men strung out behind him. Nothing to a man who is not walking.</summary>
        public void Linger(float seconds)
        {
            if (Dead || !HasOrder) return;
            _hold = Mathf.Max(_hold, seconds);
        }

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
            Hustle = false;
            EndChat();
            _hold = delay;
            SeatWhereHeStands();
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
                _link = rev;
                _cameFrom = rev.From;
                CarrySeat();   // turned round: the frame flips, his feet do not
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
            Hustle = false;
            EndChat();
            Waiting = false;   // a stride order is not queued at any light
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
            Hustle = false;
            EndChat();
            Waiting = false;   // a stride order is not queued at any light
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
            if (Target != target) { _coverLooked = false; _underFire = 0; _coverRecheckAt = 0f; }
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
            if (prefab == null) { _aimArm = null; return; }
            var animator = Tf.GetComponentInChildren<Animator>();
            Weapon = CrewArms.Attach(animator, prefab);
            _aimArm = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
        }

        // ------------------------------------------------------------------ the aim

        // The gun arm, turned at the shoulder after the animation. The pistol clips
        // aim where they were authored to aim - at the horizon of the rig they were
        // made on - and on the pack bodies that lands the barrel in the pavement a
        // few strides out. The clip cannot know where the other man stands; this does.
        Transform _aimArm;
        float _aimBlend;
        Vector3 _aimDir;

        /// <summary>Point the gun at the man he is fighting - the whole right arm,
        /// turned at the shoulder by whatever it takes for the muzzle's line to pass
        /// through the target's chest, eased in and out so the raise reads as a
        /// raise. Called by the arena from LateUpdate, once the animation has posed
        /// the arm for the frame; it holds for every pose he fights in, the squared
        /// stand and the shot on the move alike. The clips stay untouched - this is
        /// laid over the top, and blends away the moment the fight is over.</summary>
        public void AimGun(float dt)
        {
            bool aiming = !Dead && Armed && State == Mode.Engaging && _flinch <= 0f &&
                          Target != null && Target.Tf && !Target.Dead &&
                          !(InCover && _ducked);
            if (aiming)
            {
                var flat = Target.Tf.position - Tf.position;
                flat.y = 0f;
                // inside the fight's reach, and squared up enough that the arm and
                // not the whole man does the turning
                aiming = flat.magnitude <= Ballistics.Range * 1.35f &&
                         Vector3.Angle(Tf.forward, flat) < 70f;
            }
            _aimBlend = Mathf.MoveTowards(_aimBlend, aiming ? 1f : 0f, 6f * dt);
            if (_aimBlend <= 0.001f || _aimArm == null || Weapon == null) return;
            var muzzle = CrewArms.MuzzleOf(Weapon);
            var mp = muzzle != null ? muzzle.position : Weapon.position;
            var mf = muzzle != null ? muzzle.forward : Weapon.forward;
            if (aiming) _aimDir = (Target.ChestPosition - mp).normalized;
            if (_aimDir.sqrMagnitude < 1e-4f) return;
            var turn = Quaternion.FromToRotation(mf, _aimDir);
            turn.ToAngleAxis(out float angle, out var axis);
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return;
            if (angle > 180f) angle -= 360f;
            angle = Mathf.Clamp(angle, -70f, 70f) * _aimBlend;
            if (Mathf.Abs(angle) < 0.05f) return;
            _aimArm.rotation = Quaternion.AngleAxis(angle, axis) * _aimArm.rotation;
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
                    TickStride(dt, _legTo, 0.15f, hurry: Hustle);
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
        /// <summary>How near the mark a chase has to be before it will step off the
        /// pavement and cut across the road. Further out than this he runs along the
        /// kerb to get level with the man first.</summary>
        const float CrossWithin = 12f;

        /// <summary>A chase kept on the pavement, and ONLY while a drive-by is being
        /// ridden. The rest of the time the outfit and the mobs cut across the road as
        /// they always have (WalkRoute says so in as many words) - it is the drive-by
        /// that turns that into a bad scene, because the machine draws the whole rival
        /// crew out onto the main road after it and the traffic then has a gunfight to
        /// pick its way round.
        ///
        /// A man walking straight at somebody
        /// across the street walks into the carriageway and finishes the fight standing
        /// in it, which is what the drive-bys looked like: the rival crew out on the main
        /// road, the traffic picking its way round a gunfight. So while the mark is still
        /// a way off, a step that would put him on the road is turned along it instead -
        /// he runs the kerb until he is level, and crosses at the end, which is what a man
        /// does. Close in, or when the mark is himself out on the road (a rider, a man in
        /// a car), nothing is kept from him.</summary>
        Vector3 KeepToPavement(Vector3 from, Vector3 dir, float lookahead)
        {
            var net = LaneNet.Active;
            if (net == null) return dir;
            var here = net.Locate(from, out _, out float dHere, 10f);
            if (here != null && Mathf.Abs(dHere) < here.HalfRoad) return dir;   // already crossing: finish it
            var ahead = net.Locate(from + dir * lookahead, out _, out float dAhead, 10f);
            if (ahead == null || Mathf.Abs(dAhead) >= ahead.HalfRoad) return dir;
            var axis = ahead.Axis;
            axis.y = 0f;
            if (axis.sqrMagnitude < 1e-4f) return dir;
            axis.Normalize();
            return Vector3.Dot(axis, dir) >= 0f ? axis : -axis;
        }

        void TickStride(float dt, Vector3 to, float stopWithin, bool hurry = false, bool run = false,
            bool keepOffRoad = false)
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
            // the dawdle gears the walk, never the run - a man running for his life
            // does not slow down for the crew he is running from
            float pace = jog ? JogSpeed : (hurry ? Speed * HurryFactor : Speed) * PaceScale;
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
            if (keepOffRoad && CrewBike.AnyPassOn && dist > CrossWithin)
                dir = KeepToPavement(Tf.position, dir, Mathf.Max(pace * dt, 1.2f));
            _detouring = Vector3.Dot(dir, want) < 0.995f;

            float step = Mathf.Min(pace * dt, Mathf.Min(dist, clear));

            // THE FLOOR IS THE WORLD. A stride, wherever it was ordered from - a walk,
            // a march, a man running for his life - never carries a foot off the
            // ground the scene laid (WalkObstacles.City). At the hem he slides along
            // it rather than stopping dead; a man already OUT there (dealt badly, or
            // fenced in after the fact) is let walk freely, because the way back in is
            // a step like any other and freezing him out in the void helps nobody.
            if (step > 1e-4f && WalkObstacles.InCity(Tf.position) &&
                !WalkObstacles.InCity(Tf.position + dir * step))
            {
                var ax = new Vector3(dir.x, 0f, 0f);
                var az = new Vector3(0f, 0f, dir.z);
                if (ax.sqrMagnitude > 1e-6f &&
                    WalkObstacles.InCity(Tf.position + ax.normalized * step))
                    dir = ax.normalized;
                else if (az.sqrMagnitude > 1e-6f &&
                         WalkObstacles.InCity(Tf.position + az.normalized * step))
                    dir = az.normalized;
                else step = 0f;
            }

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

        /// <summary>Behind a car's tin or a bin's flank, firing over it - a harder man
        /// to hit.</summary>
        public bool InCover { get; private set; }

        /// <summary>Down behind it, head in: not shooting, and very hard to hit. Read
        /// off <see cref="InCover"/>, so it dies with the cover and no reset of it has
        /// to remember this flag.</summary>
        public bool Ducked => InCover && _ducked;

        /// <summary>Where he is headed to get behind, or stood behind. The arena reads
        /// it so that two men do not claim one flank.</summary>
        public Vector3? CoverSpot => _coverSpot;

        Vector3? _coverSpot;
        bool _ducked;           // down behind it this beat, rather than up firing
        float _coverCycle;      // what is left of that beat
        float _coverRecheckAt;  // when the arena may be asked for a spot again
        Vector3 _coverFrom;     // where the target stood when this spot was chosen

        /// <summary>The arena's answer to "where can this man duck behind, this near
        /// his target": a spot, or null. Set by whoever owns the cars and the
        /// pavement's furniture.</summary>
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
            // - and not into a wall or a car, and not off the scene's floor: a few
            // rolls for a spot he can stand on, then whatever the last roll gave
            // (he stops short of it, as near as he gets - the stride itself holds
            // the hem, so a run aimed out into the void ends at the fence, standing)
            for (int roll = 0; roll < 6; roll++)
            {
                var line = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f) * away;
                _fleeTo = Tf.position + line * Random.Range(near, far);
                if (WalkObstacles.InCity(_fleeTo) &&
                    !WalkObstacles.Occupied(_fleeTo, WalkObstacles.Radius)) break;
            }
            _fleeTo = WalkObstacles.ClampToCity(_fleeTo);
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
            // The first thing a man in a gunfight does is look for something to get
            // behind. It used to be the last - only a man on his last point of health,
            // or one with three rounds already round his ears, went looking, once, and
            // never again for that target. So two crews stood in the open trading fire
            // across a street furnished with bins, planters and phone boxes.
            //
            // Now he looks the moment he squares up, and looks again every couple of
            // seconds: when he has nothing (the street may have changed, or the fight
            // moved to where there is furniture), and when the man he is fighting has
            // walked a street's width off the line the spot was chosen against - a
            // flank is only a flank against one direction.
            if (FindCover != null)
            {
                if (!_coverSpot.HasValue && (!_coverLooked || Time.time >= _coverRecheckAt))
                {
                    _coverLooked = true;
                    _coverRecheckAt = Time.time + Random.Range(2f, 3f);
                    _coverSpot = FindCover(this, Target.Tf.position);
                    if (_coverSpot.HasValue)
                    {
                        _coverFrom = Target.Tf.position;
                        _ducked = true;                       // he arrives low
                        _coverCycle = Random.Range(0.6f, 1.1f);
                    }
                    // what the street had to offer, asked and answered - the only way
                    // to tell "he never looked" from "there was nothing to get behind"
                    if (DriveTrace.On)
                    {
                        var sb = DriveTrace.Take();
                        DriveTrace.Str(sb, "who", DisplayName);
                        DriveTrace.Bool(sb, "found", _coverSpot.HasValue);
                        DriveTrace.Num(sb, "range", dist);
                        if (_coverSpot.HasValue)
                            DriveTrace.Num(sb, "walk", Vector3.Distance(Tf.position, _coverSpot.Value));
                        DriveTrace.Row("cover", sb.ToString());
                    }
                }
                else if (_coverSpot.HasValue && Time.time >= _coverRecheckAt &&
                         (Target.Tf.position - _coverFrom).sqrMagnitude > 4f * 4f)
                {
                    _coverRecheckAt = Time.time + Random.Range(2f, 3f);
                    _coverFrom = Target.Tf.position;
                    _coverSpot = FindCover(this, Target.Tf.position);
                    if (!_coverSpot.HasValue) InCover = false;   // walked round: out in the open, then
                }
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
                else
                {
                    // At it. He is not a man welded to a bin: he goes down behind it,
                    // and comes up to shoot. Down he is silent and very hard to hit; up
                    // he is the same man he always was, firing the same rounds through
                    // the same code below. That trade is the whole of it.
                    InCover = true;
                    _coverCycle -= dt;
                    if (_ducked)
                    {
                        if (_coverCycle <= 0f)
                        {
                            _ducked = false;
                            _coverCycle = Random.Range(1.6f, 2.4f);    // up for a round or two
                            _fireTimer = Mathf.Min(_fireTimer, 0.35f); // rises, squares, fires
                        }
                        else
                        {
                            // turned at the fight even while down, so the rise reads
                            if (dist > 1e-3f)
                                Tf.rotation = Quaternion.RotateTowards(Tf.rotation,
                                    Quaternion.LookRotation(toTarget / dist), 240f * dt);
                            SetPose(HasPose(PoseCrouch) ? PoseCrouch
                                    : HasPose(PosePistolIdle) ? PosePistolIdle : PoseIdle);
                            TickBlend(dt);
                            return;                                    // no round goes off down there
                        }
                    }
                    else if (_coverCycle <= 0f)
                    {
                        _ducked = true;
                        // down for less than he is up: a man who spends the fight with
                        // his head in is a man the fight waits for
                        _coverCycle = Random.Range(0.8f, 1.4f);
                    }
                }
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
                // and only once the arm has actually come up (AimGun's blend): a round
                // that leaves the barrel while the gun still hangs at the hip is the
                // shot into the pavement the player kept seeing - and a flinch mid-walk
                // is a gun pointing anywhere at all
                if (dist <= range && _fireTimer <= 0f && _flinch <= 0f && _aimBlend >= 0.5f &&
                    Vector3.Angle(Tf.forward, toTarget) < 40f)
                {
                    _fireTimer = Ballistics.Interval * OnTheMove;
                    if (HasPose(PoseShoot))
                    {
                        RestartPose(PoseShoot);
                        _shootHold = Mathf.Min(PoseLength(PoseShoot), 0.45f);
                    }
                    Fired?.Invoke(this);
                }
                // the chase keeps to the pavement unless the man he is after is out on
                // the road himself, in which case that is where the fight is
                TickStride(dt, Target.Tf.position, range * RangeFactor, hurry: true,
                    keepOffRoad: !OnCarriageway(Target.Tf.position)); // a quick walk, no running
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

            // shoot only once squared up - a man firing over his shoulder reads wrong -
            // and once the gun is actually raised on him (the aim blend): rising from a
            // duck, or fresh out of a flinch, the barrel spends a beat coming up, and a
            // round let off during it goes into the ground the clip was authored at
            float off = Vector3.Angle(Tf.forward, toTarget);
            if (_fireTimer <= 0f && off < 25f && _aimBlend >= 0.5f)
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

        /// <summary>Is this point out on the asphalt rather than on the pavement?
        /// The arena reads it too: a man STOOD on the asphalt is walked off it.</summary>
        internal static bool OnCarriageway(Vector3 p)
        {
            var net = LaneNet.Active;
            if (net == null) return false;
            var road = net.Locate(p, out _, out float d, 10f);
            return road != null && Mathf.Abs(d) < road.HalfRoad;
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
        // ------------------------------------------------------------------ the roam

        /// <summary>How long a man stands about before he would rather be somewhere
        /// else. Long enough that a corner still reads as a corner being held, short
        /// enough that the town is not a waxworks.</summary>
        static readonly Vector2 RoamRest = new Vector2(9f, 30f);

        /// <summary>How far he goes when he goes: enough to be a walk somewhere, not so
        /// far that a crew stops being a crew standing in the same few streets.</summary>
        const float RoamNear = 18f, RoamFar = 95f;

        /// <summary>May this man wander off on his own when he is bored? A hood may
        /// not: a lieutenant's crew keeps together, so only the lieutenant strolls and
        /// the arena's tether (DemoCrews.TickCohesion) brings his men along. On for a
        /// man who answers to nobody (a deserter, a lone idler).</summary>
        public bool RoamsAlone = true;

        /// <summary>The farthest a stroll of his own may take him. A lone man may
        /// cross the quarter; a lieutenant holding a corner gets a stretch of the legs
        /// round that corner, because his whole crew comes with him.</summary>
        public float RoamReach = RoamFar;

        /// <summary>The ground this man's crew HOLDS - where his strolls are anchored.
        /// Without it every stroll starts from wherever the last one ended, which is a
        /// random walk: a lieutenant bored once a half-minute drifts across the whole
        /// town over a run, his hoods dutifully in tow, and the corner the crew was
        /// dealt to stands empty. With a post the stroll is measured FROM THE POST, so
        /// however many he takes, the crew orbits its own ground for the whole run.
        /// Set where the crew is dealt, moved when an order moves the crew.</summary>
        public Vector3? Post;

        /// <summary>Kept off the fence by this much, so nobody picks the very edge of the
        /// town and then spends the whole walk pressed against it.</summary>
        const float RoamInset = 6f;

        float _roamIn = -1f;

        /// <summary>Somewhere else to be. The ground he may pick is the whole city and
        /// not the pavements - OrderAcross draws the way round the walls and he walks its
        /// corners. Three things rule a spot out: outside the town (the fence the builder
        /// laid, WalkObstacles.City), inside something (Occupied - a wall is the one thing
        /// he cannot cross), and the carriageway itself. He may CROSS a road, and does;
        /// but a man STOOD in a running lane is one car's problem and then the whole
        /// queue's, so he is never sent to one.</summary>
        void TryRoam()
        {
            var centre = Post ?? Tf.position;
            for (int i = 0; i < 12; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                float d = Random.Range(Mathf.Min(RoamNear, RoamReach * 0.5f), RoamReach);
                var to = centre + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                if (!WellInsideCity(to)) continue;
                if (WalkObstacles.Occupied(to, WalkObstacles.Radius)) continue;
                if (OnCarriageway(to)) continue;
                OrderAcross(to, Random.Range(0f, 0.6f));
                return;
            }
            // hemmed in this time round - stand a while longer and look again
            _roamIn = Random.Range(RoamRest.x, RoamRest.y) * 0.5f;
        }

        /// <summary>In the town, and not on its hem.</summary>
        static bool WellInsideCity(Vector3 p)
        {
            var city = WalkObstacles.City;
            if (city.Count == 0) return true;
            for (int i = 0; i < city.Count; i++)
            {
                var r = city[i];
                if (p.x >= r.xMin + RoamInset && p.x <= r.xMax - RoamInset &&
                    p.z >= r.yMin + RoamInset && p.z <= r.yMax - RoamInset) return true;
            }
            return false;
        }

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

            // and now and then he has had enough of standing here
            if (_roamIn < 0f) _roamIn = Random.Range(RoamRest.x, RoamRest.y);
            _roamIn -= dt;
            if (_roamIn <= 0f)
            {
                _roamIn = Random.Range(RoamRest.x, RoamRest.y);
                if (Target == null && RoamsAlone) TryRoam();
                if (State != Mode.Standing) return;
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
            CarrySeat();   // the new frame, the same feet - stepping onto a stretch is not a snap
            State = Mode.Homing;
        }

        // He sets off from where his FEET are. The graph walks a man down his link's
        // line plus his lateral, but stood about he drifts off that line - eased apart
        // from the man beside him, shoved along the kerb at a light - and the first
        // Move of a new order used to snap him back onto it: a hop of a metre or two
        // the player read as a respawn. His metre and lateral are re-read off his true
        // position instead, so the first step of an order is a step.
        void SeatWhereHeStands()
        {
            if (Waiting) return;   // stood at a gate: the order's own Waiting branch reroutes him
            CarrySeat();
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

        /// <summary>What the trace says this man is doing. His FAMILY is on it (f0 is
        /// the outfit) because the fault rows are read by family afterwards - "whose men
        /// stall" is a different question from "do men stall", and the id alone cannot
        /// answer it.</summary>
        protected override string TraceState()
            => State + " f" + Faction + (Target != null ? " at " + Target.DisplayName : "");

        /// <summary>A crew man is going somewhere only under an order; stood on his
        /// corner with his hands in his pockets, he is where he means to be. And a
        /// man SPENDING A BEAT under an order - the hood's polite stagger, the
        /// dawdle of a man ahead of his crew, the boss lingering for his men - is
        /// standing on purpose too: the stall clock (TracePed) must not count him,
        /// or every deliberate wait reads as a man wedged on a bin.</summary>
        protected override bool Moving => HasOrder && _hold <= 0f;

        public string StatusLine => State switch
        {
            Mode.Standing => Retreating ? "Gone" : Alert ? "On alert - shots heard" : "Standing by",
            Mode.Walking => "On the move, heading " + PatrolInfo.Heading(Tf),
            Mode.Striding => "On the move, heading " + PatrolInfo.Heading(Tf),
            Mode.Homing => "Almost there",
            Mode.Engaging => Target != null
                ? Ducked ? "Down behind cover - " + Target.DisplayName + " out there"
                  : InCover ? "Shooting from cover at " + Target.DisplayName
                  : "Shooting at " + Target.DisplayName
                : "Engaging",
            Mode.Fleeing => Retreating ? "Getting out of here" : "Running for it",
            Mode.Riding => "In the car",
            Mode.Dead => "Down",
            _ => string.Empty,
        };
    }
}
