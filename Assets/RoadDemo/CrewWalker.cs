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
    public class CrewWalker : PedestrianAgent, RiderSpill.IBody
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

        /// <summary>Somebody to step round. A man DOWN is not - the living walk over
        /// the spot he fell on, which is what a body in the street looks like - and
        /// neither is a man in a seat: he rides at the car's position, so leaving him
        /// in would have every pedestrian in town giving way to a moving car twice,
        /// once for the car and once for each man inside it.</summary>
        protected override bool InCrowd => !Dead && !Riding;

        /// <summary>Whom he is shooting at, or null.</summary>
        public CrewWalker Target { get; private set; }

        /// <summary>When he last actually LAID EYES on the man he is shooting at -
        /// the arena stamps it every frame the mark is in sight (DemoCrews.TickCombat).
        /// A mark that steps behind a wall is not dropped on the frame it disappears
        /// (a man rounding the corner of a van would flicker the gun up and down every
        /// stride); it is dropped when this has gone stale. Carried on the man rather
        /// than in a register on the arena so it dies with him.</summary>
        public float SawMarkAt = -100f;

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

        /// <summary>Is the SIDEWALK GRAPH placing his feet this frame? A man walking a
        /// stretch has his position rebuilt every frame out of metre-plus-lateral, so
        /// anything that writes his transform from outside - the elbow pass - is undone
        /// by the next Move and merely makes him shudder. Off the graph he steps by
        /// hand, and a shove stays put.</summary>
        public bool GraphDriven => State == Mode.Walking || State == Mode.Homing;

        /// <summary>Stood at a crossing waiting on the light. The arena's tether reads
        /// it: a crew does not split over a red - a man his crew has already crossed
        /// away from cuts over after them instead of standing on the zebra alone.</summary>
        public bool AtLight => _link != null && Waiting;

        /// <summary>Get out of somebody's way. What the arena's elbow pass
        /// (DemoCrews.Separate) hands its metres to instead of writing the transform.
        ///
        /// THE OVERLAP IS ALWAYS RESOLVED, one way or the other - which is the half
        /// that was missing first time and cost more than it bought. Near the camera
        /// he TAKES A STEP, because a man eased off his neighbour while he plays a
        /// stand is a man gliding sideways. Too far off to read, he is simply moved:
        /// nobody can see a slide they cannot see. Only a man who is near AND already
        /// busy is left for a frame, because he is about to be able to step.
        ///
        /// Leaving every refusal unresolved, as this did at first, means two crews
        /// that meet stand inside each other for as long as they are both busy - and
        /// then everybody else brakes into the knot, which is the jam the player got
        /// instead of the slide.</summary>
        public void EaseAside(Vector3 worldDir, float metres)
        {
            // A MAN WHOSE LEGS ARE ALREADY GOING IS NEVER HANDED A SHUFFLE. The
            // shuffle clips are authored from a standstill - both feet planted, the
            // weight shifting off one of them - so laid over a stride they read as a
            // man dipping in the middle of his walk and then carrying on, which is
            // exactly what the player saw ("covek hoda i samo cucne u toku hoda i
            // nastavi"). He is already moving: the metres hide inside his own stride,
            // so they are simply written.
            if (LegsMoving) { Nudge(worldDir, metres); return; }
            if (BeginSidestep(worldDir)) return;
            if (Detailed) return;                 // near, busy: he steps next frame
            Nudge(worldDir, metres);
        }

        void Nudge(Vector3 worldDir, float metres)
        {
            var to = Tf.position + worldDir * metres;
            if (!WalkObstacles.Occupied(to, WalkObstacles.Radius) && WalkObstacles.InCity(to))
                Tf.position = to;
        }

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

        /// <summary>Quicker FEET on the current stride - a hood strung out behind his
        /// crew catching it up. A walk, never a run: the man wants to be level with his
        /// boss, not to arrive out of breath. Cleared by the next order, and by the
        /// tether the moment he is back with his crew.</summary>
        public bool Hustle;

        /// <summary>The player asked for this order TWICE (a double right click), which
        /// is the only thing in the town that puts a crew into a run over open ground.
        /// Kept apart from <see cref="Hustle"/> because the two are cleared by different
        /// things - the hurry dies the moment a laggard is back with his crew, and the
        /// order does not: a hood at his boss's shoulder is level with him and still has
        /// four hundred metres to cover. Cleared by the next order, like everything else
        /// about the last one.
        ///
        /// A stroll is never urgent: a man who has had enough of his corner and walks
        /// somewhere else (TryRoam) sets off without this, and walks the whole way.</summary>
        public bool Urgent;

        /// <summary>How much ground still to cover before a walk stops being worth it.
        /// Below it a man walks the errand; above it he breaks into a run and walks the
        /// last <see cref="RunSettle"/> metres in, so he arrives at his crew's pace
        /// instead of skidding to a halt on the spot.</summary>
        const float RunBeyond = 25f, RunSettle = 8f;

        /// <summary>The crowd's brake as the GAIT reads it - the raw figure smoothed,
        /// so a man decides to break stride rather than dithering at every shoulder.</summary>
        float _gaitBrake = 1f;

        /// <summary>How fast the smoothed brake follows the raw one, per second.</summary>
        const float GaitEase = 2f;

        /// <summary>The hysteresis on the drop: a running man holds the jog until he is
        /// braked to GaitDrop of the band's floor, and a walking man does not pick the
        /// run back up until the way is nearly clear (GaitBack) - two thresholds, so
        /// the gait cannot flap between them at the edge of a queue.</summary>
        const float GaitDrop = 0.7f, GaitBack = 0.95f;

        bool _runningLeg;

        /// <summary>Are his legs playing a RUN this frame - the jog or the sprint,
        /// the blend's target and not the order. What the audit holds against the
        /// ground he actually covers. The sprint counts because it is the same fault
        /// one storey worse: a man whose legs are flat out and who is crossing the
        /// ground at a walk is skating harder, not less.</summary>
        public bool JoggingPose => CurrentPose == PoseJog || CurrentPose == PoseSprint;

        /// <summary>Is he running this frame? Only under a hurried order, only with a
        /// real distance left of it, and only if his body has a run to play - a man
        /// without the clip walks quickly, which is what he always did.
        ///
        /// The distance is to the FAR END of the errand and not to the next corner, or
        /// he would drop to a walk at every turn of a way round a block.</summary>
        bool Running()
        {
            // ONLY the order the player asked for twice. NOT Hustle: a hood catching
            // his crew up gets quicker feet, and that is all he ever wanted - putting
            // him into a jog for it means somebody is trotting somewhere at all times,
            // which is a town in a panic rather than a town with an outfit in it.
            if (!Urgent || !HasPose(PoseJog) || State != Mode.Striding)
                return RunWhile(false);
            var to = _legEnd - Tf.position;
            to.y = 0f;
            return RunWhile(to.magnitude > (_runningLeg ? RunSettle : RunBeyond));
        }

        /// <summary>Hold the run's own state: it is picked up the frame it starts (and
        /// only then - re-dealing his stride at every corner would put a hitch in his
        /// cadence each time the way turned) and dropped when the reason for it goes.
        /// Every reason a man runs passes through here, so there is one place that
        /// knows whether he is running.</summary>
        bool RunWhile(bool running)
        {
            if (running && !_runningLeg) BreakIntoRun();
            _runningLeg = running;
            return running;
        }

        /// <summary>THE PAVEMENTS RUN TOO. A crew sent somewhere in the city walks the
        /// sidewalk graph - that is what a player's order does, and it is deliberate:
        /// the men keep their formation and their lanes and cross where a man crosses.
        /// But the graph carried everybody at one number and that number was his walk,
        /// so four hundred metres of pavement could only ever be walked, and the run
        /// added to the free stride never showed where the player actually looks.
        ///
        /// The same rule decides it as off the graph (<see cref="Running"/>): under a
        /// hurried order, with real ground still to cover, and not while the tether is
        /// holding him back for the others. What changes is only how it is spent - the
        /// pace the graph carries him at (<see cref="GraphPace"/>) and the clip the
        /// crossfade drifts to. Everything else about walking a stretch is untouched:
        /// his lane, the furniture he leans round, the lights he ignores.</summary>
        void GearGraphWalk(float dt)
        {
            var to = Destination - Tf.position;
            to.y = 0f;
            bool run = Urgent && HasPose(PoseJog) && PaceScale > 0.95f &&
                       to.magnitude > (_runningLeg ? RunSettle : RunBeyond);
            // THE CROWD'S BRAKE OUTRANKS THE ORDER. A man braked well under the jog
            // band cannot be kept honest by clip rate alone - held at quarter pace his
            // legs stride ground he is not covering, the overstriding skate the player
            // was watching. Under GaitDrop of the band's floor he drops to the walk,
            // whose rate follows any pace (HoldWalkRate), and picks the run back up
            // when the way opens. The brake the GAIT listens to is smoothed first:
            // the crowd's raw figure collapses over less than a metre (Notice), so
            // read straight it flapped the gait once a second and the blend never
            // settled - a man decides to break stride, he does not dither. A mild
            // mismatch (the band floor holding the feet a shade quick) is kept in
            // preference to a gait change; only a real queue drops him.
            float brake = PaceScale * Mathf.Max(CrowdHold, 0.25f);
            _gaitBrake = dt > 0f ? Mathf.MoveTowards(_gaitBrake, brake, GaitEase * dt) : brake;
            if (run && JogSpeed * _gaitBrake < RunRateMin * ClipPace(PoseJog, JogClipPace) *
                (_runningLeg ? GaitDrop : GaitBack))
                run = false;
            LocomotionPose = RunWhile(run) ? PoseJog : PoseWalk;
            // What he actually covers, which is not what he was dealt: the dawdle and
            // the man in front of him both gear the graph's pace. The crowd's brake is
            // last frame's - Tick reads it after this - and one frame of lag on a clip
            // rate is nothing to see.
            float pace = GraphPace(false) * brake;
            // the walk's own rate is Move's business, for every walker in the city at
            // once (HoldWalkRate); only the run is this class's to keep in step
            if (run)
                SetPoseSpeed(PoseJog, Mathf.Clamp(
                    pace / ClipPace(PoseJog, JogClipPace), RunRateMin, RunRateMax) * _runJitter);
        }

        /// <summary>Metres a second the sidewalk carries him at. A crossing's hustle is
        /// for a man walking one; a man already running needs nothing added.</summary>
        protected override float GraphPace(bool gated) =>
            _runningLeg ? JogSpeed : base.GraphPace(gated);

        /// <summary>A runner reads the stretch further out and takes the crowd's shove
        /// at half strength - the walk's figures at a run's pace made corrections land
        /// late and at full width, which is a man weaving down the pavement.</summary>
        protected override float FreeLineAhead => _runningLeg ? 4f : base.FreeLineAhead;
        protected override float PushGain => _runningLeg ? 0.5f : base.PushGain;

        /// <summary>Start the run somewhere along its own clip and at a rate of his own -
        /// so a crew that sets off together is a crew of runners, not one runner
        /// copied five times.</summary>
        void BreakIntoRun()
        {
            _runJitter = Random.Range(0.94f, 1.06f);
            // only when the run is not already on him: re-seeding a clip a man is
            // visibly mid-stride of pops his legs, and the tether can re-order a
            // running man at any moment
            if (CurrentPose != PoseJog) ScatterPhase(PoseJog);
        }

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
            if (Spilling) return;   // in the air off a machine: he is the spill's until he lands
            if (Dead || Riding || link == null || link.Length <= 0.01f || _link == null) return;
            Target = null;
            _coverSpot = null;
            InCover = false;
            _returnTo = null;
            Hustle = false;
            Urgent = false;
            _runningLeg = false;
            _sprinting = false;
            _keepingLow = false;
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
            if (Spilling) return;   // in the air off a machine: he is the spill's until he lands
            if (Dead) return;
            Target = null;
            _coverSpot = null;
            InCover = false;
            Hustle = false;
            Urgent = false;
            _runningLeg = false;
            _sprinting = false;
            _keepingLow = false;
            EndChat();
            Waiting = false;   // a stride order is not queued at any light
            _hold = delay;
            point.y = Tf.position.y;
            _legs.Clear();
            _legTo = point;
            // one leg, so its corner IS its far end - and the run reads the far end
            _legEnd = point;
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
            if (Spilling) return;   // in the air off a machine: he is the spill's until he lands
            if (Dead) return;
            Target = null;
            _coverSpot = null;
            InCover = false;
            Hustle = false;
            Urgent = false;
            _runningLeg = false;
            _sprinting = false;
            _keepingLow = false;
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
            _runningLeg = false;
            _sprinting = false;
            _keepingLow = false;
            // a man with a fight on is nobody's laggard: the tether steps back from
            // him (it skips anyone with a target), so the dawdle it left on him would
            // otherwise hold for the whole fight - and a dawdled man walks where he
            // should be closing at a run
            SetPace(1f);
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

        /// <summary>How far off a man will still put his gun up at somebody riding past
        /// - on a saddle or behind a windscreen. Further than any gun in the town shoots
        /// well, on purpose: he cannot walk up to a machine doing fifty, so it is this or
        /// he stands and watches it go.</summary>
        public static float PassingShot = 25f;

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
                // not the whole man does the turning.
                //
                // A MAN ON A MACHINE OR IN A CAR IS SHOT AT FURTHER OFF. Every other
                // fight in the town is fought at the gun's own reach because a man who
                // wants a closer shot WALKS UP - and that is exactly what nobody can do
                // to a motorcycle (TickEngage refuses to close on a mounted mark). With
                // the same reach on both, a crew shot up by a pass standing off twenty
                // metres never raised its guns at all: the aim blend is what lets a
                // round leave the barrel, and it only came up inside eight metres. The
                // rounds that answer a drive-by are long, wild and mostly miss - the
                // falloff sees to that (DemoCrews.Resolve) - and they are the whole
                // scene.
                float reach = Target.Riding || Target.Astride
                    ? Mathf.Max(Ballistics.Range * 1.35f, PassingShot)
                    : Ballistics.Range * 1.35f;
                aiming = flat.magnitude <= reach &&
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
            CancelJoin();   // nobody finishes a turn on the way down
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
            // KEEPING LOW IS THIS FRAME'S DECISION, NOT A STATE. It is set by the one
            // branch that wants it - the last few metres to a flank with rounds in the
            // air - and that branch re-decides it every frame, so it is cleared here
            // and never anywhere else. Held as a state it went stale the instant the
            // fight moved on: the man reached his bin, the mark walked out of reach,
            // the cover was dropped, and he then WALKED THE REST OF THE RUN BENT
            // DOUBLE, because the gait Loco chooses reads this flag. Which is exactly
            // what the player watched - men who crouch as they set off and men who
            // never stand back up.
            _keepingLow = false;
            // IN THE AIR OFF A MOTORCYCLE. The spill writes his transform and holds a
            // take on him; all he wants from here is the blend that shows it. Every
            // branch below would fight it - the riding one re-asserts the seated pose
            // every frame, which cancels the act slot the fall is playing in, so a man
            // thrown off used to tumble in whatever he was sitting in.
            // A DEAD one still goes through the Dead branch below: it writes no
            // transform, and it is what drops the gun out of his hand and holds the
            // death on its last frame.
            if (Spilling && !Dead)
            {
                TickBlend(dt);
                return;
            }
            switch (State)
            {
                case Mode.Dead:
                    TickBlend(dt);
                    if (HasPose(PoseDeath))
                    {
                        float len = PoseLength(PoseDeath), at = PoseTime(PoseDeath);
                        // the gun leaves the hand part-way down and lies where it fell -
                        // but not while he is still in the air off a machine, or it is
                        // left hanging at head height over the road he is falling into
                        if (!_gunDropped && !Spilling && at >= len * 0.45f) DropGun();
                        if (at >= len - 0.03f) HoldPose(PoseDeath);
                    }
                    else if (!_gunDropped && !Spilling) DropGun();
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
                    TickStride(dt, _legTo, 0.15f, hurry: Hustle, run: Running());
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
                        _sprinting = false;
                        _shaken = Random.Range(8f, 14f); // out of it a while, then game again
                        // pulled up, and the first thing he does is look back at what
                        // he ran from - the beat that says he has not forgotten it
                        PlayAction(CrewKit.BackLooks);
                    }
                    return;
                }

                default: // Walking, Homing - the graph
                    if (HoldingBeat(dt)) return;
                    GearGraphWalk(dt);
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

        /// <summary>How far off its natural rate the run clip may be played before the
        /// feet read wrong. Narrow on purpose: the pace follows the CLIP here, not the
        /// other way round, so a wide band buys a man who covers the ground at a speed
        /// his legs are visibly not keeping.</summary>
        const float RunRateMin = 0.9f, RunRateMax = 1.12f;

        float _runRate = 1f;

        /// <summary>The band a man on foot in this town may run in, whatever any clip
        /// claims about itself. THE PACE IS READ OFF THE CLIP - a run clip that covers
        /// twice the ground moves the man twice as fast - so one bad draw out of the
        /// wardrobe used to send a hood down the pavement at five metres a second while
        /// his crew jogged at three. The clip is allowed to set the LOOK; it is not
        /// allowed to decide how fast a man in this city can be.</summary>
        const float JogSlowest = 2.5f, JogQuickest = 3.8f;

        public void SetJog(float speed)
        {
            float natural = ClipPace(PoseJog, JogClipPace);
            _runRate = Mathf.Clamp(speed / natural, RunRateMin, RunRateMax);
            JogSpeed = Mathf.Clamp(_runRate * natural, JogSlowest, JogQuickest);
            // and if the band had to pull him back, his feet come with him rather than
            // keeping a stride he is no longer covering
            _runRate = JogSpeed / natural;
            SetPoseSpeed(PoseJog, _runRate);
        }

        /// <summary>How much quicker a man walks when he is closing on a fight - the
        /// hurried walk he finishes an approach at, once the run has brought him near.</summary>
        const float HurryFactor = 1.3f;

        /// <summary>How far out of his gun's reach a man runs the approach in, and how
        /// far in he keeps running once he has started - as multiples of that reach, so
        /// a rifleman and a man with a shotgun each break at their own distance. Apart
        /// on purpose: a mark that backs off a step must not switch him between a walk
        /// and a run once a second.</summary>
        const float RunToFight = 1.8f, RunOffFight = 1.25f;

        /// <summary>Metres to a flank worth running for, with rounds in the air.</summary>
        const float RunToCover = 5f;

        /// <summary>Metres out from a flank a man under fire finishes the walk to it
        /// bent double. Inside RunToCover on purpose - the crouch and the run are
        /// exclusive, so the two figures must not overlap.</summary>
        const float CrouchWithin = 4f;

        // ------------------------------------------------------------------ the stride

        /// <summary>Metres looked down the line for what is in the way - a car's
        /// length, near enough: he leans off it early and passes it in one curve.</summary>
        const float Lookahead = 3f;

        int _steerSide;      // which way round the last thing in his way he went (WalkObstacles)
        Vector3 _strideDir;  // the line he stepped along last frame; zero at the start of a leg
        float _blockedFor;   // seconds stood on this leg with nowhere to step
        bool _detouring;     // this frame's step was off the line to the spot, round something
        bool _strideJog;     // was he jogging the stride last frame (the gait's own hysteresis)

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

        /// <summary>How hard the people in his way bend his line, in metres across it
        /// per unit of shove. The crowd's own reader hands out at most 0.9 of shove, so
        /// this is a lean of about a quarter turn at the very worst and a couple of
        /// degrees for somebody merely passing.</summary>
        const float CrowdLean = 0.6f;

        /// <summary>The least of his pace the crowd may leave him. NEVER ZERO: a man
        /// stopped dead behind another is a bollard his own boss then brakes behind,
        /// and the leg's stall clocks would read the wait as a man wedged in a bin. He
        /// crawls and leans off instead, and gets by.</summary>
        const float CrowdFloor = 0.25f;

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
            // A MAN BEING HELD BACK DOES NOT RUN. The dawdle (PaceScale) is how the
            // tether keeps a quick hood level with his boss, and it gears the walk
            // only - so a crew that broke into a run had no brake on it at all and
            // strung out down the street exactly as it used to. Gearing the RUN
            // instead would be worse: the clip's rate is dealt per man and a jog
            // played over a walking step is a man skating. He drops out of the run
            // and walks until the dawdle is lifted, which is what a man does when
            // he is told to wait for the others.
            bool jog = run && HasPose(PoseJog) && PaceScale > 0.95f;
            // and a man running for his life is never dawdled anyway - the tether
            // leaves a panicked man alone
            float pace = jog ? RunPace
                : (hurry ? Speed * HurryFactor : Speed) * PaceScale *
                  (_keepingLow ? CrouchFactor : 1f);
            var want = delta / dist;

            // THE PEOPLE ARE PART OF THE GROUND. WalkObstacles knows the walls, the
            // furniture and the traffic, and nothing whatever about anybody on foot -
            // so a man striding over open ground walked clean through his own crew,
            // which is what the player was watching. The crowd's own reader answers
            // who is in the way (one bucket, every walker in the scene: citizens, the
            // outfit, the mobs, the law), and it answers it for the pavements too, so
            // the two halves of the town cannot disagree about who is standing where.
            //
            // He leans off them and comes off his pace behind them, and the obstacle
            // steer below then runs on the line he has actually chosen - so the
            // clearance it reports belongs to the step he takes, and not to a line
            // he has already turned off.
            ReadCrowd(dt, want);
            var line = want;
            if (Mathf.Abs(CrowdPush) > 0.001f)
            {
                var across = new Vector3(want.z, 0f, -want.x);
                // a runner threads the same people at twice the closing speed, so a
                // full shove each pass is a man weaving down the street - half of it
                // buys the same clearance over the extra metres his pace covers
                line = (want + across * (CrowdPush * CrowdLean * (jog ? 0.5f : 1f))).normalized;
            }
            float held = Mathf.Max(CrowdHold, CrowdFloor);
            pace *= held;
            // THE CROWD'S BRAKE OUTRANKS THE RUN here too: braked under the band the
            // run clip reads at (RunRateMin), his feet cannot follow the ground - he
            // walks those strides instead (the walk's rate follows any pace), and the
            // run comes back when the way opens. A touch more asked to re-enter than
            // to stay, so the gait does not flicker at the band's edge.
            if (jog && pace < RunRateMin * (_strideJog ? 1f : 1.1f) * RunClipPace)
            {
                jog = false;
                pace = (hurry ? Speed * HurryFactor : Speed) * PaceScale * held *
                       (_keepingLow ? CrouchFactor : 1f);
            }
            _strideJog = jog;

            Vector3 dir;
            float clear;
            if (WalkObstacles.Occupied(to, WalkObstacles.Radius))
            {
                dir = line;
                clear = WalkObstacles.Clear(Tf.position, line, WalkObstacles.Radius, dist);
            }
            else
                // a runner reads the ground further out - at the walk's three metres
                // his corrections come late and hard and he zig-zags thing to thing
                dir = WalkObstacles.Steer(Tf.position, line, _strideDir, WalkObstacles.Radius,
                    Mathf.Min(jog ? Lookahead * 2f : Lookahead, dist), ref _steerSide, out clear);
            if (keepOffRoad && CrewBike.AnyPassOn && dist > CrossWithin)
                dir = KeepToPavement(Tf.position, dir, Mathf.Max(pace * dt, 1.2f));
            // stepping round somebody counts as going round something: the leg's stall
            // clock must not read a man giving way on a busy pavement as a man wedged
            _detouring = Vector3.Dot(dir, want) < 0.995f || held < 0.9f;

            // capped at the hitch ceiling too: a stalled frame (large dt) must not fling
            // him a lane sideways as he steers round something - it reads as a teleport.
            // GaitGain is the join's: a man setting off eases up to this over the start
            // clip, and one pulling up eases off over the stop clip. Applied HERE and
            // not to `pace` above, so the gait's own thresholds (is he running, at what
            // clip rate) are judged on the pace he is actually settling to.
            float step = Mathf.Min(Mathf.Min(pace * GaitGain * dt, MaxStepPerFrame),
                Mathf.Min(dist, clear));

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

            // he turns to the line he is walking; boxed in, he at least faces the spot.
            // A join owns his heading while it runs - the 90 and 180 starts ARE the
            // turn, and a man swung round to the new line before the clip has played
            // its first step is a man who stumbles on the spot.
            if (!Joining)
                Tf.rotation = Quaternion.Slerp(Tf.rotation,
                    Quaternion.LookRotation(step > 1e-4f ? dir : want), 8f * dt);

            bool moving = step > 1e-4f;
            if (jog && moving)
            {
                LocomotionPose = RunPose;
                BlendLocomotion(dt, true);
                // the run keeps step with the ground he actually covers: the crowd
                // takes pace off him, and a jog played at its own rate over a
                // shortened step is a man skating. Held inside the rates a run clip
                // reads at (RunRateMin/Max) - past those it is a moon-walk - and his
                // own hair off the beat kept, so a crew never runs in lockstep.
                SetPoseSpeed(RunPose, Mathf.Clamp(
                    pace / RunClipPace, RunRateMin, RunRateMax) * _runJitter);
            }
            else if (!moving) Loco(dt, false);
            else
            {
                Loco(dt, true);
                // the gait clip keeps step with the pace: quicker feet for the hurried
                // walk, and the crouched shuffle keyed to its own much shorter stride
                if (LocomotionPose == PoseCrouchWalk)
                    SetPoseSpeed(PoseCrouchWalk,
                        Mathf.Clamp(pace / ClipPace(PoseCrouchWalk, 1.3f), 0.7f, 1.4f));
                else HoldWalkRate(pace);
            }
        }

        /// <summary>How much of his walk a man keeps while he is crossing ground bent
        /// double. A crouched shuffle is not a walk with the head down; it is slow,
        /// and looking slow is most of what says he is under fire.</summary>
        const float CrouchFactor = 0.6f;

        /// <summary>Which run he is in: the sprint when he is running for his life
        /// and his body has one, else the jog every other errand uses. ONE place
        /// decides it, so the pose, the clip rate and the metres a second can never
        /// come from different clips - which is the fault CrewKit.Runs is a whole
        /// essay about.</summary>
        int RunPose => _sprinting && HasPose(PoseSprint) ? PoseSprint : PoseJog;
        float RunClipPace => _sprinting && HasPose(PoseSprint)
            ? ClipPace(PoseSprint, SprintClipPace) : ClipPace(PoseJog, JogClipPace);
        float RunPace => _sprinting && HasPose(PoseSprint) ? _sprintSpeed : JogSpeed;

        /// <summary>Running for his life rather than to somewhere - the one thing in
        /// the town that reaches for the sprint. Cleared by every order, like every
        /// other thing about the last one.</summary>
        bool _sprinting;
        float _sprintSpeed = 5.2f;

        /// <summary>The pack's sprint covers about this much ground a second at
        /// playback 1 - the figure used only when the clip does not say.</summary>
        const float SprintClipPace = 5.2f;

        /// <summary>The band a man may sprint in, the run band's rule one storey up:
        /// the clip sets the look, the town sets how fast a man can be.</summary>
        const float SprintSlowest = 4.2f, SprintQuickest = 6.2f;

        /// <summary>Break into the flat-out run, if he has one. The pace comes off the
        /// clip and is then held to the town's band, the same trade as SetJog.</summary>
        void BreakIntoSprint()
        {
            _sprinting = HasPose(PoseSprint);
            if (!_sprinting) return;
            float natural = ClipPace(PoseSprint, SprintClipPace);
            _sprintSpeed = Mathf.Clamp(natural * Random.Range(0.94f, 1.06f),
                SprintSlowest, SprintQuickest);
            SetPoseSpeed(PoseSprint, _sprintSpeed / natural);
            if (CurrentPose != PoseSprint) ScatterPhase(PoseSprint);
        }

        // ------------------------------------------------------------------ the leg

        // Has this leg come to its end short of the spot? A leg ends at the spot, or
        // as near as he can get: stood still with nowhere to step (boxed in by cars
        // and walls), walking straight at it and getting no nearer (another man is
        // stood on it; the crowd will not let him through), or round something for so
        // long that he has plainly lost it. While he is going round a thing and still
        // moving he is given his time: a car's flank takes a while to pass.
        float _bestLegDist = float.MaxValue, _stall, _wander;

        /// <summary>A hair off his own run rate, re-dealt whenever he sets off. Two men
        /// who break into a run in the same second are otherwise in step to the frame,
        /// which is the thing that reads as a machine rather than as men.</summary>
        float _runJitter = 1f;

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

        // ------------------------------------------------------- off the machine

        /// <summary>He is in the air, or in the road, having come off a motorcycle -
        /// the spill owns his transform and his pose until it settles (RiderSpill).
        ///
        /// He is left in <see cref="Mode.Riding"/> throughout, and that is the whole
        /// trick: every question the town asks about a man it may order about - is he
        /// a body in the carriageway, has he a place in his crew's line, may he be sent
        /// somewhere - is already written as "not while he is riding". A new state
        /// would have to be added to all of them and would be missed in one.</summary>
        public bool Spilling { get; private set; }

        /// <summary>RiderSpill.IBody. His root is what the fall throws; his riding pose
        /// is the thing that must stop writing him first; and a man the town has already
        /// killed comes off limp, because CrewWalker.Kill started the crowd's own death
        /// on him the moment the round landed and nothing here improves on that.</summary>
        Transform RiderSpill.IBody.Root => Tf;
        BikePose RiderSpill.IBody.Pose => Tf != null ? Tf.GetComponent<BikePose>() : null;
        AnimationClip RiderSpill.IBody.Playing => Dead ? null : Take;
        bool RiderSpill.IBody.Finished => Dead || TakeFinished;
        bool RiderSpill.IBody.AlreadyDying => Dead;

        void RiderSpill.IBody.Play(AnimationClip clip, bool loop, float fade, float speed, float at)
        {
            // A DEAD MAN IS NOT DRESSED TWICE. His death is running in the pose graph
            // already (Kill), held on its last frame by the Dead branch of TickCrew, and
            // a wardrobe death laid over it in the act slot would be the same body dying
            // two ways at two rates. The fade is the graph's own (a fixed crossfade) and
            // is not the caller's to set here.
            if (Dead) return;
            PlayTake(clip, loop, speed, at);
        }

        /// <summary>He has left the machine: hand him to the spill. Called by CrewBike,
        /// which is the only thing that knows he was on one.</summary>
        public void BeginSpill()
        {
            Spilling = true;
            RidingAim = false;
            Target = null;
            State = Dead ? Mode.Dead : Mode.Riding;
        }

        /// <summary>The spill is over: back on his own feet, or lying where he stopped.
        /// The take comes off him here and nowhere else - a held one never ends by
        /// itself, and a man still wearing it swallows every pose he is given after.</summary>
        public void EndSpill()
        {
            if (!Spilling) return;
            Spilling = false;
            EndTake();
            if (!Dead) SetRiding(false);
        }

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
            // whatever he was in the middle of on the pavement, he is in a seat now:
            // the car writes his transform every frame and a turn still owing degrees
            // would fight it for the same rotation
            if (on) CancelJoin();
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
            SpendLook(150f, dt);
            SetPose(Armed && HasPose(PosePistolIdle) ? PosePistolIdle : PoseIdle);
            TickBlend(dt);
        }

        /// <summary>Turn him onto the heading he last decided to look at, and forget
        /// it once he is there. Big turns are TAKEN, in steps (the base's TurnToward
        /// picks); a glance is a glance and is simply swivelled, as it always was.
        /// The one place the standing men's heading is written, so the join layer and
        /// the plain ease can never both be turning the same man.</summary>
        void SpendLook(float degreesPerSecond, float dt)
        {
            if (float.IsNaN(_lookYaw)) return;
            TurnToward(Quaternion.Euler(0f, _lookYaw, 0f) * Vector3.forward,
                degreesPerSecond, dt);
            if (!TurningOnSpot &&
                Mathf.Abs(Mathf.DeltaAngle(Tf.eulerAngles.y, _lookYaw)) < 0.5f)
                _lookYaw = float.NaN;
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
            TurnToward(to, 200f, dt);
            SetPose(HasPose(PoseShout) ? PoseShout : Armed && HasPose(PosePistolIdle) ? PosePistolIdle : PoseIdle);
            TickBlend(dt);
        }

        /// <summary>Off the scene for good: a long run away from here, and gone.</summary>
        public void Retreat(Vector3 from)
        {
            if (Dead || Spilling || Riding) return;
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
            if (Dead || Spilling || _nerveRolled || Health > 1) return;
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
            // NOT OFF A MOVING MACHINE, AND NOT OUT OF A MOVING CAR. A man whose nerve
            // goes while he is riding was put into Mode.Fleeing on the saddle: the
            // vehicle went on writing his transform, so he sat there looking like a
            // pillion while the town thought he was running for his life - and the
            // machine's own guns went on firing him (CrewBike.TickGuns), which is a man
            // fleeing and shooting at once. His nerve is asked again the moment he is
            // back on his feet (DemoCrews.Rejoin, and the car's DriverLost).
            if (Dead || Spilling || Riding) return;
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
            // random stride, at a rate of his own - not the crew's one run, in step.
            // A MAN RUNNING FOR HIS LIFE SPRINTS. It is the one place in the town that
            // reaches for the flat-out clip: everywhere else a man is running TO
            // something and has to stay with his crew, which is exactly why the
            // sprint is kept out of the gait pool (CrewKit.Runs).
            _hold = Random.Range(0.1f, 0.6f);
            _keepingLow = false;
            BreakIntoRun();
            BreakIntoSprint();
            // whatever the tether was holding him back for, it is not that now. The
            // dawdle drops a man out of a run (TickStride), and a man walking away
            // from a gunfight because his boss wanted him level is not a scene.
            SetPace(1f);
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
                    // THE LAST FEW METRES ARE MADE LOW. A man who has had rounds round
                    // his ears and is a stride or two off a bin does not stroll up to
                    // it at full height - he goes down and crosses the rest bent
                    // double, which is both what a man does and, from the near camera,
                    // the only part of getting behind something the player can read.
                    // Kept inside RunToCover so the crouch and the run never argue.
                    _keepingLow = _underFire > 0 && HasPose(PoseCrouchWalk) &&
                                  gap.magnitude <= CrouchWithin;
                    // a bin two streets' width off with rounds in the air is got to at
                    // a run; one at his elbow is stepped behind
                    TickStride(dt, spot, 0.4f, hurry: true,
                        run: RunWhile(!_keepingLow && gap.magnitude > RunToCover));
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

            // A MAN ON A MACHINE IS NOT CHASED ON FOOT. Shot at from a passing
            // motorcycle, a crew used to set off after it - five men walking down the
            // street behind a bike doing fifty, for as long as it kept moving. Nobody
            // does that. What a man does is get behind something and fire back, which is
            // exactly what he does here with the closing taken away: the cover look above
            // still runs, and the shooting below still runs the moment the machine comes
            // back into his reach.
            bool mounted = Target.Riding || Target.Astride;
            bool closing = !mounted && !_coverSpot.HasValue &&
                           (_wasClosing ? dist > range * RangeFactor : dist > range * 1.15f);
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
                // is a gun pointing anywhere at all.
                //
                // NEVER AT A RUN. A running man's gun arm is in the stride, muzzle
                // down more of the time than not, and a round out of it reads wrong
                // however it is gated - the player's rule is that a man who is running
                // does not fire at all. He fires from the hurried WALK the approach
                // drops him into (RunOffFight hysteresis), which is the answering-fire
                // the closing shot exists for.
                if (dist <= range && !_runningLeg && _fireTimer <= 0f && _flinch <= 0f &&
                    _aimBlend >= 0.5f &&
                    Vector3.Angle(Tf.forward, toTarget) < 40f && BarrelOn(Target))
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
                // the road himself, in which case that is where the fight is.
                //
                // WELL out of his reach he runs it in; inside a stride or two of it he
                // walks the rest, because a man who sprints to the line and stops dead
                // reads as a puppet being placed. The two figures are apart on purpose,
                // so a mark backing off a metre does not switch him between the two
                // every second.
                TickStride(dt, Target.Tf.position, range * RangeFactor, hurry: true,
                    run: RunWhile(dist > range * (_runningLeg ? RunOffFight : RunToFight)),
                    keepOffRoad: !OnCarriageway(Target.Tf.position));
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
            if (_fireTimer <= 0f && off < 25f && _aimBlend >= 0.5f && BarrelOn(Target))
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

        /// <summary>Inside what angle of the line to the mark's chest the barrel must
        /// actually lie for a round to leave it. The audit already judges every shot
        /// by this line (CrewAudit "aimlow", 35 deg); this is the same rule enforced
        /// at the trigger, a shade tighter. What it is for is the man firing on the
        /// move: the run clip swings the gun arm through the stride, AimGun's clamp
        /// cannot always bring the muzzle up from the bottom of the swing, and a round
        /// let off there is the shot into the pavement the player keeps seeing - a
        /// running man now holds the round for the beat of the stride the barrel is
        /// actually on him, which is also when it reads.</summary>
        const float BarrelOnLimit = 30f;

        /// <summary>Is last frame's posed barrel near enough the line to this man's
        /// chest? Point blank the angle means nothing - the round goes in regardless -
        /// which is the audit's own let-off.</summary>
        bool BarrelOn(CrewWalker mark)
        {
            var to = mark.ChestPosition - MuzzlePosition;
            return to.magnitude < 2f || Vector3.Angle(MuzzleForward, to) < BarrelOnLimit;
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
        //
        // Routed through the base's BlendLocomotion rather than posing by hand, so
        // the foot-planted joins (setting off, pulling up) run for a man striding
        // over open ground exactly as they do for one walking a stretch. Keeping
        // low, the gait he walks in is the crouched one.
        void Loco(float dt, bool walking)
        {
            LocomotionPose = _keepingLow && HasPose(PoseCrouchWalk) ? PoseCrouchWalk : PoseWalk;
            BlendLocomotion(dt, walking);
        }

        /// <summary>Crossing the last of the ground to a bin with rounds already in
        /// the air: he goes the rest of the way down, which is both what a man does
        /// and the only part of getting behind something that the player can read.</summary>
        bool _keepingLow;

        /// <summary>The line the start clip is chosen against - where he is about to
        /// go, which off the sidewalk graph is his own leg's end and not any stretch.
        /// Without this a man who sets off across a lot picks a start out of the
        /// stretch he happens to be stood near, and turns the wrong way to it.</summary>
        protected override Vector3 JoinHeading
        {
            get
            {
                Vector3 mark;
                switch (State)
                {
                    case Mode.Fleeing: mark = _fleeTo; break;
                    case Mode.Striding: mark = _legTo; break;
                    case Mode.Engaging:
                        mark = _coverSpot ?? (Target != null && Target.Tf
                            ? Target.Tf.position : Tf.position);
                        break;
                    default: return base.JoinHeading;
                }
                var to = mark - Tf.position;
                to.y = 0f;
                return to.sqrMagnitude > 1e-4f ? to : base.JoinHeading;
            }
        }

        // ------------------------------------------------------------------ at ease

        float _idleTimer = 2f;
        float _lookYaw = float.NaN;
        CrewWalker _chatPartner;
        float _chatLeft, _floorLeft;   // the word, and whose turn it is to have it
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
            _floorLeft = Random.Range(2.5f, 4.5f);
            _lookYaw = float.NaN;
            // now and then it is not a word, it is an argument: the man with the
            // floor squares right up instead of talking with his hands
            _arguing = Random.value < ArgueChance;
            _gestureIn = Random.Range(0.6f, 2f);
            // and it opens the way one opens - a nod at the other man
            if (speaksFirst) PlayAction(CrewKit.Greet);
        }

        /// <summary>How many of the outfit's corner conversations are rows. Low: an
        /// outfit where every second exchange is a squaring-up is a comedy.</summary>
        const float ArgueChance = 0.18f;

        bool _arguing;
        float _gestureIn, _fidgetIn = -1f, _leanUntil;

        public void EndChat()
        {
            if (_chatPartner == null) return;
            _chatPartner = null;
            ChatCooldown = Random.Range(8f, 20f);
            _idleTimer = Random.Range(2f, 5f);
            _fidgetIn = Random.Range(2f, 6f);
            // a word ends the way one ends. Not after a row: nobody waves off a man
            // he has just squared up to.
            if (!_arguing && !Dead && State == Mode.Standing) PlayAction(CrewKit.Waves);
            _arguing = false;
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
                    _floorLeft -= dt;
                    if (_floorLeft <= 0f)
                    {
                        _speaking = !_speaking;
                        _floorLeft = Random.Range(2.5f, 4.5f);
                        _gestureIn = Random.Range(0.4f, 1.6f);
                    }
                    var to = _chatPartner.Tf.position - Tf.position;
                    to.y = 0f;
                    // face him, and TURN to face him if he is behind - two men who
                    // stop for a word on a corner are the most looked-at pair of
                    // bodies in the town, and a swivel gives the whole thing away
                    TurnToward(to, 120f, dt);
                    // the hands: the man with the floor points up the street or
                    // shakes his head at all of it, the other nods along. A row is
                    // the same conversation with one of them squared right up.
                    if (!Acting && !Joining)
                    {
                        _gestureIn -= dt;
                        if (_gestureIn <= 0f)
                        {
                            _gestureIn = Random.Range(2.2f, 5f);
                            if (_arguing && _speaking)
                                HoldAction(CrewKit.AggressiveLoop, Random.Range(1.6f, 3.2f));
                            else
                                PlayAction(_speaking ? CrewKit.SpeakGestures : CrewKit.ListenGestures);
                        }
                    }
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

            // a glance around: a new heading now and then, turned to at a stroll -
            // and a real turn is TAKEN, with steps under it (SpendLook). This is the
            // single most-watched thing a crew does, because holding a corner is what
            // they spend the run doing, and a man rotating on the spot like a lamp on
            // a turntable is the whole town's illusion gone in one shot.
            _idleTimer -= dt;
            if (_idleTimer <= 0f)
            {
                // on watch: a man on a corner keeps turning his head - a look this
                // way, a look that way, back to where he was
                _idleTimer = Random.Range(2f, 5.5f);
                if (Random.value < 0.7f)
                    _lookYaw = Tf.eulerAngles.y + Random.Range(-110f, 110f);
            }
            SpendLook(45f, dt);
            TickIdleLife(dt);
            Loco(dt, false);
        }

        /// <summary>What a man does with the minutes he spends holding a corner: puts
        /// his back against the wall when there is one behind him, and otherwise fills
        /// the wait - checks the watch, kicks at the pavement, takes a swig, has a
        /// look up the street. NEVER while anything is actually happening: the whole
        /// layer stands down for a join, an order, an alert and a fight, and for a man
        /// too far off the camera to be read at all (PlayAction's own gate).</summary>
        void TickIdleLife(float dt)
        {
            if (Joining || !float.IsNaN(_lookYaw)) return;

            // the wall: if he is leant, stay leant until the lease runs out
            if (_leanUntil > Time.time) return;
            if (Acting) return;

            if (_fidgetIn < 0f) _fidgetIn = Random.Range(3f, 9f);
            _fidgetIn -= dt;
            if (_fidgetIn > 0f) return;
            _fidgetIn = Random.Range(5f, 14f);

            // THE LEAN IS PARKED (LeanChance 0). A lean is authored against a flat
            // vertical face at one particular distance, and there is no way to know
            // offline whether the wall a man happens to have behind him is at that
            // distance - so when it is wrong he does not read as leaning, he reads as
            // sagging backwards into a squat, and the player kept seeing men crouch
            // for no reason. The wardrobe and the wall test stay; turn LeanChance back
            // up only with eyes on the scene.
            if (LeanChance > 0f && Random.value < LeanChance && CrewKit.LeanLoops.Count > 0 &&
                WalkObstacles.WallAt(Tf.position - Tf.forward * LeanReach, 0.25f) &&
                HoldAction(CrewKit.LeanLoops[Random.Range(0, CrewKit.LeanLoops.Count)],
                    Random.Range(6f, 16f)))
            {
                _leanUntil = Time.time + 6f;
                return;
            }
            PlayAction(CrewKit.CrewFidgets);
        }

        /// <summary>How often a bored man on a corner with a wall behind him uses it,
        /// and how far behind him "behind him" is - a pace, so he leans on the thing
        /// he is stood against and not on one across the pavement.</summary>
        const float LeanChance = 0f, LeanReach = 0.55f;

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

        /// <summary>The outfit does not queue at a light. The crowd waits at a red for a
        /// gap it can cross in (PedestrianAgent.MayEnter); a crew told to be somewhere
        /// steps onto the zebra whatever the signal says - the same as the march does off
        /// the graph, and the traffic brakes for a body in the road either way. This is
        /// what makes an ORDERED crossing go on red while the men stay in their formation
        /// on the sidewalk graph, rather than the whole crew leaving it to cut across the
        /// ground (which strung them out over the block). The crowd's own MayEnter is
        /// untouched - only the outfit ignores the light.</summary>
        protected override bool MayEnter(PedLink link) => true;

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
