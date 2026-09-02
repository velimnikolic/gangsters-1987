using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Entities;
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
    public partial class CrewWalker : PedestrianAgent, RiderSpill.IBody
    {
        public enum Mode { Standing, Walking, Homing, Striding, Engaging, Fleeing, Riding, Dead }

        public Mode State { get; private set; } = Mode.Standing;

        public CrewWalker() { Tag = "crew"; }

        /// <summary>The roster id of the man this figure stands for (rivals carry
        /// their own negative ids - they are on nobody's books).</summary>
        public int CharacterId;

        /// <summary>His Combat, in half-steps, cached off the ledger when the crews
        /// were dealt. Cached rather than looked up per shot because Resolve runs on
        /// every round of every fight in the city; it cannot go stale, because a man
        /// only ever improves at the day tick and that bumps the personnel version the
        /// re-deal watches (DemoCrews.Update). A rival, who is on nobody's books,
        /// keeps the middling default.</summary>
        public int CombatHalfSteps = 6;
        public string DisplayName = "";
        public bool IsLieutenant;

        /// <summary>The pack prefab this body was cast from - the ledger recasts a man
        /// on promotion (a lieutenant wears a suit), and the street must follow.</summary>
        public GameObject SourcePrefab;

        /// <summary>The shared city body sample applied to this visual body, if any.</summary>
        public PedestrianAnthropometryStamp Anthropometry;

        public float OverlayHeight => Anthropometry
            ? Anthropometry.OverlayHeight
            : IsLieutenant ? 2.25f : 2.05f;

        /// <summary>0 is the outfit; anything else is a rival mob.</summary>
        public int Faction;

        // ------------------------------------------------------------------ arms

        public Transform Weapon { get; private set; }
        public EquipmentKind WeaponKind { get; private set; }
        public GameObject WeaponPrefab { get; private set; }

        /// <summary>The gun is OUT - in his fist, where it can be aimed and fired.
        /// This is what every part of the fight means by armed: AimGun raises the arm
        /// off it, the fire gates wait on the raise, and a man without it cannot put a
        /// round anywhere. A man walking the town with a piece under his coat is NOT
        /// this - see <see cref="Carrying"/>.</summary>
        public bool Armed => Weapon != null;

        /// <summary>He has a gun to his name - the ledger's, in his hand or under his
        /// coat. This is the question to ask of a man being PICKED for something: sent
        /// into a fight, put on a car, dealt onto a saddle. Asking <see cref="Armed"/>
        /// there would pass over every man in the town who has not drawn yet, which
        /// since the gun stays out of sight until there is something to point it at
        /// is nearly all of them.</summary>
        public bool Carrying => WeaponPrefab != null;
        public CrewArms.Stats Ballistics { get; private set; }

        /// <summary>The pieces held with both hands while running. Sidearms, including
        /// the machine pistol, keep the locomotion clip's ordinary arm swing.</summary>
        public int Health = 3;

        /// <summary>His hands are up. Set by the crew giving itself up to the law
        /// (DemoCrews.GiveUp) and read HERE, at the one gate every gun in the game
        /// passes through: a man who has surrendered never wants his piece out, so
        /// nothing that happens round him - a shot, an alarm, a mark walking past -
        /// can put it back in his fist while he waits to be taken.</summary>
        public bool Surrendered;
        public int MaxHealth = 3;
        public bool Dead => State == Mode.Dead;

        /// <summary>Somebody to step round. A man DOWN is not - the living walk over
        /// the spot he fell on, which is what a body in the street looks like - and
        /// neither is a man in a seat: he rides at the car's position, so leaving him
        /// in would have every pedestrian in town giving way to a moving car twice,
        /// once for the car and once for each man inside it.</summary>
        protected override bool InCrowd => !Dead && !Riding;

        /// <summary>Whom he is shooting at, or null.</summary>
        CrewWalker _target;
        /// <summary>The man he has his gun on. Setting it drops any CAR he was on:
        /// a man shoots at a man or at the tin, never at both, and ONE assignment
        /// enforces that rather than every one of the dozen places that clear a
        /// target having to remember the other mark exists.</summary>
        public CrewWalker Target
        {
            get => _target;
            private set { _target = value; CarMark = null; }
        }

        /// <summary>The CAR he is emptying his gun into, when there is no man to shoot
        /// at. A machine stood at the kerb with nobody in it is still worth shooting up
        /// - and it is the one mark in the town that cannot shoot back, so nothing here
        /// looks for cover or waits for it to duck.</summary>
        public CrewCar CarMark { get; private set; }

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
        int _firePose = PoseShoot;
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

        // A cohesion order is not a fresh player march. It closes the space left by a
        // fight or a funnel by bringing one hood back to his formation slot. The audit
        // needs that narrow distinction while the order is live: a stopped lieutenant
        // can be standing in the carriageway after combat, and the shortest honest way
        // back to him may briefly run along it; the different slots also make the men
        // fan inward on different headings. No other order inherits the licence.
        bool _fallingIn;
        public bool FallingIn => _fallingIn &&
            (State == Mode.Walking || State == Mode.Homing || State == Mode.Striding);

        internal void MarkFallingIn() => _fallingIn = HasOrder && State != Mode.Fleeing;
        void ClearFallingIn() => _fallingIn = false;

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

        /// <summary>How much ground still to cover before a double-click run starts,
        /// and how close to the mark it settles back to a walk. The old 25 m gate made
        /// most visible orders ignore the double click entirely; the click is the
        /// player's explicit pace order, so only the final couple of steps are walked.</summary>
        const float RunBeyond = 3f, RunSettle = 1.2f;

        /// <summary>The crowd's brake as the GAIT reads it - the raw figure smoothed,
        /// so a man decides to break stride rather than dithering at every shoulder.</summary>
        float _gaitBrake = 1f;

        /// <summary>How fast the smoothed brake follows the raw one, per second.</summary>
        const float GaitEase = 2f;

        /// <summary>
        /// The hysteresis on the drop: a running man holds the jog until he is braked
        /// to GaitDrop of the band's floor, and a walking man does not pick the run
        /// back up until the way is nearly clear (GaitBack) - two thresholds, so the
        /// gait cannot flap between them at the edge of a queue.
        ///
        /// GaitDrop IS A SKATE TOLERANCE, and it was set where the feet visibly beat
        /// the ground. Below the band's floor the playback rate is clamped UP to it
        /// (RunRateMin), so a man held at 0.7 of the floor covered 3.0 m/s with his
        /// legs playing 3.95 - thirty per cent of pure skate, held deliberately, for
        /// as long as the crowd kept him there. At 0.85 the worst case is fifteen,
        /// which is the mild mismatch the comment below always claimed to be keeping.
        ///
        /// It is not set to 1.0 - no skate at all - because it cannot be: the city
        /// caps a jog at JogQuickest 3.8 m/s while the imported male run clip's own
        /// pace is 4.3845, so RunRateMin x clip = 3.95 is ABOVE the fastest this town
        /// lets a man jog. A floor of 1.0 would mean nobody ever runs. That mismatch
        /// is a real one and it is not this constant's to settle.
        /// </summary>
        const float GaitDrop = 0.85f, GaitBack = 0.95f;

        /// <summary>The shared admission floor for a clip-driven gait. A new gait
        /// needs an almost clear lane; once established it survives a stronger crowd
        /// brake. Keeping this calculation shared prevents graph walking and free
        /// strides from disagreeing about the same Mixamo clip.</summary>
        internal static bool GaitPaceAllowedModel(float pace, float clipPace,
            float rateFloor, bool holdingGait) =>
            pace >= rateFloor * clipPace * (holdingGait ? GaitDrop : GaitBack);

        bool _runningLeg;

        /// <summary>Are his legs playing a RUN this frame - the jog or the sprint,
        /// the blend's target and not the order. What the audit holds against the
        /// ground he actually covers. The sprint counts because it is the same fault
        /// one storey worse: a man whose legs are flat out and who is crossing the
        /// ground at a walk is skating harder, not less.</summary>
        public bool JoggingPose => CurrentMotionIsRunning;

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
            if (run && !GaitPaceAllowedModel(JogSpeed * _gaitBrake,
                    ClipPace(PoseJog, JogClipPace), RunRateMin, _runningLeg))
                run = false;
            LocomotionPose = RunWhile(run) ? VisibleJogPose : VisibleWalkPose;
            // What he actually covers, which is not what he was dealt: the dawdle and
            // the man in front of him both gear the graph's pace. The crowd's brake is
            // last frame's - Tick reads it after this - and one frame of lag on a clip
            // rate is nothing to see.
            float pace = GraphPace(false) * brake;
            // the walk's own rate is Move's business, for every walker in the city at
            // once (HoldWalkRate); only the run is this class's to keep in step
            if (run)
            {
                if (LocomotionPose == PoseRifleJog)
                    GearVisibleRifleGait(LocomotionPose, pace, JogClipPace, _runJitter);
                else
                    SetPoseSpeed(PoseJog, Mathf.Clamp(
                        pace / ClipPace(PoseJog, JogClipPace), RunRateMin, RunRateMax) * _runJitter);
            }
            else GearVisibleRifleGait(LocomotionPose, pace, WalkClipPace);
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

        /// <summary>
        /// PedLink.Free was sampled before residential views stream in. Prove the
        /// outfit's actual graph step against the live ledger; civilians retain the
        /// cheap sampled path, while an ordered crew member may never spend a stale
        /// link line through a newly arrived cafe or its furniture.
        /// </summary>
        protected override bool GraphStepClear(Vector3 from, Vector3 to)
        {
            if (State != Mode.Walking && State != Mode.Homing) return true;
            return !WalkObstacles.Standing(to, WalkObstacles.Radius) &&
                   !WalkObstacles.BlocksStanding(from, to, WalkObstacles.Radius);
        }

        /// <summary>A stale graph link is abandoned before any movement is committed.
        /// Continue to the same order destination through the live A* ground map; if
        /// no safe route exists, BeginAcross refuses it and leaves the man standing.</summary>
        protected override void GraphStepBlocked(Vector3 wanted)
        {
            if (State != Mode.Walking && State != Mode.Homing) return;
            var destination = OrderDestination;
            // Do not replace one member's common sidewalk trip with a private route
            // straight to its destination. Preserve the route the graph had already
            // chosen and use A* only to detour back onto that same corridor.
            _graphCorridor.Clear();
            if (!CopyPlannedRoute(_graphCorridor) || _graphCorridor.Count < 2)
            {
                RefuseUnsafeAcross();
                return;
            }
            // CopyPlannedRoute begins with the walker's feet for preview drawing. That
            // is not a corridor anchor. In particular, if a streamed prop appeared
            // around him, retaining it would make the recovery connector walk back
            // into the occupied point it had just escaped.
            int passed = SharedCursorAfter(_graphCorridor, 0, Tf.position);
            if (passed > 0) _graphCorridor.RemoveRange(0, passed);
            // A streamed table or venue can cover the graph node itself. Routing to
            // that occupied node is impossible and used to stop the walker even when
            // the next node on the same graph corridor was clear. Skip only occupied
            // anchors; A* still has to prove the detour to the first usable one.
            while (_graphCorridor.Count > 0 &&
                   WalkObstacles.Standing(_graphCorridor[0], WalkObstacles.Radius))
                _graphCorridor.RemoveAt(0);
            if (_graphCorridor.Count == 0)
            {
                RefuseUnsafeAcross();
                return;
            }
            bool relocated = false;
            if (WalkObstacles.Standing(Tf.position, WalkObstacles.Radius))
            {
                const float MaxRecoveryStep = 2.5f;
                var from = Tf.position;
                if (!WalkObstacles.TryClearStandingSpot(from, WalkObstacles.Radius,
                        _graphCorridor[0], out var free, MaxRecoveryStep))
                {
                    RefuseUnsafeAcross();
                    return;
                }
                free.y = from.y;
                Tf.position = free;
                relocated = true;
            }
            bool accepted = BeginAcross(
                destination, _graphCorridor, 0f, keepOffRoad: false);
            _graphCorridor.Clear();
            if (accepted)
                _preferredSteerSide = CrowdPreferredSide;
            else if (relocated)
                // RefuseUnsafeAcross normally retains a valid graph seat. Relocation
                // made this seat stale, so leave it and let DemoCrews reseat the real
                // feet before a future graph order.
                LeaveGraphOrder();
        }

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

        /// <summary>The place this whole order ends. <see cref="Destination"/> is the
        /// current corner while a free-ground route is being walked; an intent overlay
        /// needs the final formation spot instead.</summary>
        public Vector3 OrderDestination => State == Mode.Striding ? _legEnd : Destination;

        /// <summary>Copies the remaining route of the current foot order into a
        /// caller-owned buffer. The list follows the same sidewalk route table or
        /// obstacle-route corners the walker is already using; it never replans or
        /// changes the order.</summary>
        public bool CopyPlannedRoute(List<Vector3> into)
        {
            if (into == null) return false;
            into.Clear();
            if (Tf == null || !HasOrder || Riding || Dead) return false;

            AddPreviewPoint(into, Tf.position);

            if (State == Mode.Striding)
            {
                if (_legs.Count > 0)
                    for (int i = Mathf.Clamp(_legAt, 0, _legs.Count - 1); i < _legs.Count; i++)
                        AddPreviewPoint(into, _legs[i]);
                else
                    AddPreviewPoint(into, _legTo);
                AddPreviewPoint(into, _legEnd);
                return into.Count > 1;
            }

            var final = OrderDestination;
            if (_link == null || State == Mode.Homing || _link == _destFwd || _link == _destBack)
            {
                AddPreviewPoint(into, final);
                return into.Count > 1;
            }

            // The live walker is already travelling along _link toward its To node.
            // From there the same node->link table used by ChooseLink says every turn.
            var node = _link.To;
            AddPreviewPoint(into, node.Pos);
            for (int leg = 0; leg < 256 && node != null; leg++)
            {
                if (_destFwd != null && (node == _destFwd.From || node == _destFwd.To))
                {
                    AddPreviewPoint(into, final);
                    return into.Count > 1;
                }
                if (_route == null || !_route.TryGetValue(node, out var toward) || toward == null)
                    break;
                AddPreviewPoint(into, toward.To.Pos);
                node = toward.To;
            }

            // A malformed or temporarily stale graph still shows the honest part it
            // knows. The final marker is drawn separately, so no fake straight line is
            // drawn through whatever made the graph stop.
            return into.Count > 1;
        }

        static void AddPreviewPoint(List<Vector3> into, Vector3 point)
        {
            if (into.Count == 0 || (into[into.Count - 1] - point).sqrMagnitude > 0.01f)
                into.Add(point);
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
            ClearFallingIn();
            ClearSharedCorridor();
            _throughWall = false;
            _watching = false;
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
            ClearFallingIn();
            ClearSharedCorridor();
            LeaveGraphOrder();
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
            _pendingAcrossPlan = false;
            _preferredSteerSide = 0;
            BeginLeg();
            State = Mode.Striding;
        }

        /// <summary>
        /// THROUGH THE DOOR. The one walk in the game that is allowed past the ground's
        /// solids, and it exists because a shop's interior is not walkable ground: a
        /// building is registered whole as a box (WalkObstacles.Block), so a man ordered
        /// to a point inside one is steered along its wall for ever and stops a couple of
        /// metres short of the glass. That is what a player watched every time he sent men
        /// to lean on a shopkeeper - the crew standing on the pavement while the wire said
        /// the owner had answered.
        ///
        /// So the passage is walked, not pathed: the same stride, the same clip, the same
        /// pace, with the obstacle map left out for its length. It is the doorway's own
        /// order and nothing else's - <see cref="DoorBeat"/> issues it for the step in and
        /// the step back out, over a couple of metres, and it lapses by itself
        /// (<see cref="ThroughGrace"/>) so no later order can inherit a man who walks
        /// through walls.
        /// </summary>
        public void OrderThroughDoorway(Vector3 point, float delay = 0f)
        {
            OrderToPoint(point, delay);
            if (State != Mode.Striding)
                return;
            _throughWall = true;
            _throughUntil = Time.time + ThroughGrace;
        }

        /// <summary>The passage is over: the walls are his again. Called the moment the
        /// threshold is crossed, so the licence lasts the doorway and not a second of
        /// the walk after it.</summary>
        public void EndDoorway() => _throughWall = false;

        /// <summary>The most a doorway passage may last before the man walks by the
        /// ordinary rules again. A threshold is two or three metres; anything past this
        /// is not a passage any more.</summary>
        public const float ThroughGrace = 12f;

        /// <summary>Whether the stride currently ignores the walls - true only inside a
        /// doorway passage, and only until its grace runs out.</summary>
        bool Crossing => _throughWall && Time.time <= _throughUntil;

        bool _throughWall;
        float _throughUntil;

        // the corners of a way across the city, and which one he is walking at
        readonly List<Vector3> _legs = new List<Vector3>();
        readonly List<Vector3> _connectorLegs = new List<Vector3>();
        // Dispatch preflight is deliberately separate from the live legs. DemoCrews
        // validates every member before it starts any of them, so one impossible hood
        // cannot leave half a crew already walking away.
        readonly List<Vector3> _preflightLegs = new List<Vector3>();
        readonly List<Vector3> _preflightScratch = new List<Vector3>();
        // A dispatched member owns a copy: DemoCrews reuses its scratch route as soon
        // as the order is dealt. _sharedAt is the next common corner he must rejoin;
        // connector corners in _legs are deliberately not part of this progress.
        readonly List<Vector3> _sharedCorridor = new List<Vector3>();
        readonly List<Vector3> _graphCorridor = new List<Vector3>();
        int _sharedAt;
        int _legAt, _replans;
        int _legsVersion;
        Vector3 _legEnd;
        bool _pendingAcrossPlan;

        /// <summary>Be there, and never mind the pavements.
        ///
        /// The crowd keeps to the sidewalk graph and waits at its lights because that
        /// is what a city looks like. The outfit does not: told to be somewhere, a man
        /// cuts over the lot, across the road against the light, down the gap between
        /// two buildings. The one thing he cannot do is walk through a wall, so the way
        /// is drawn round the walls first (WalkRoute) and he walks its corners; the
        /// cars and the crowd he steers past as he goes, like any other stride.
        ///
        /// No safe way at all - walled in, or a mark stood inside something - and the
        /// order is refused; a failed A* is never treated as permission to cross it.</summary>
        public bool OrderAcross(Vector3 point, float delay = 0f, bool keepOffRoad = false) =>
            BeginAcross(point, null, delay, keepOffRoad);

        /// <summary>Walk the same already-planned corner corridor as the rest of this
        /// crew, then peel off to this man's own final formation spot. Dispatch passes
        /// the common interior only (not the leader-only final point), and this method
        /// appends <paramref name="point"/> after it. Every shared point is copied into
        /// this walker's private route, so the caller may immediately reuse its scratch
        /// list.</summary>
        public bool OrderAcrossVia(Vector3 point, IReadOnlyList<Vector3> sharedWay,
            float delay = 0f, bool keepOffRoad = false)
        {
            if (sharedWay == null || sharedWay.Count == 0)
                return OrderAcross(point, delay, keepOffRoad);
            return BeginAcross(point, sharedWay, delay, keepOffRoad);
        }

        /// <summary>Prove this member's complete join/corridor/arrival route without
        /// changing his current order. Whole-crew dispatch uses this as a transaction
        /// preflight, then commits every already-proved member in the same frame.</summary>
        internal bool CanOrderAcrossVia(Vector3 point, IReadOnlyList<Vector3> sharedWay,
            bool keepOffRoad = false)
        {
            if (Spilling || Dead || Tf == null) return false;
            point.y = Tf.position.y;
            return CopySharedWayModel(sharedWay, Tf.position, point,
                _preflightLegs, _preflightScratch, keepOffRoad,
                StaticChordClear, WalkRoute.Plan);
        }

        /// <summary>Copy every common corner still ahead of this walker. A hood's owned
        /// corridor was already trimmed before dispatch, so its final entry is a real
        /// common corner and must not be discarded during cohesion recovery.</summary>
        internal bool CopyRemainingSharedWay(List<Vector3> into)
        {
            if (into == null) return false;
            into.Clear();
            if (State != Mode.Striding || _sharedCorridor.Count == 0) return false;
            return CopyRemainingSharedWayModel(_sharedCorridor, _sharedAt, into);
        }

        internal static bool CopyRemainingSharedWayModel(
            IReadOnlyList<Vector3> sharedWay, int sharedAt, List<Vector3> into)
        {
            if (into == null) return false;
            into.Clear();
            int count = sharedWay != null ? sharedWay.Count : 0;
            for (int i = Mathf.Clamp(sharedAt, 0, count); i < count; i++)
                into.Add(sharedWay[i]);
            return into.Count > 0;
        }

        bool BeginAcross(Vector3 point, IReadOnlyList<Vector3> sharedWay,
            float delay, bool keepOffRoad)
        {
            if (Spilling) return false;   // in the air off a machine: he is the spill's until he lands
            if (Dead) return false;
            ClearFallingIn();
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
            _legsOffRoad = keepOffRoad;
            _preferredSteerSide = sharedWay != null ? CrowdPreferredSide : 0;
            _legs.Clear();
            if (sharedWay != null)
            {
                RememberSharedCorridor(sharedWay, point.y);
                // A leader corridor proves only the leader's own first and last
                // chords. Each member may plan a connector TO that corridor, but a
                // failed join is not permission to choose an unrelated route around
                // the other side of the block.
                if (!BuildRemainingWay(Tf.position))
                {
                    RefuseUnsafeAcross();
                    return false;
                }
                _pendingAcrossPlan = false;
                _legAt = 0;
                _legTo = _legs.Count > 0 ? _legs[0] : point;
            }
            else
            {
                ClearSharedCorridor();
                // Hoods already have a deliberate beat before they follow their boss.
                // Plan during that beat, on their own frames, instead of blocking one
                // mouse-down. Whole-crew dispatch normally supplies a shared way, so
                // this individual plan is for standalone orders and recovery fallback.
                _pendingAcrossPlan = delay > 0.001f;
                if (!_pendingAcrossPlan)
                {
                    if (!BuildIndividualWay(Tf.position, point, keepOffRoad))
                    {
                        RefuseUnsafeAcross();
                        return false;
                    }
                    _legAt = 0;
                    _legTo = _legs.Count > 0 ? _legs[0] : point;
                }
                else
                {
                    _legAt = 0;
                    _legTo = point;
                }
            }
            // Keep a valid pavement seat until the off-graph route has actually been
            // proved. A rejected route leaves the man standing where he was, still
            // reseatable from that real link rather than detached by a failed order.
            if (!_pendingAcrossPlan) LeaveGraphOrder();
            var far = point - Tf.position;
            far.y = 0f;
            _acrossBest = far.magnitude;
            _legsVersion = WalkObstacles.Version;
            if (DriveTrace.On)
                DriveTrace.Event("walk", DisplayName, _legs.Count > 0
                    ? $"a way across: {_legs.Count} corners, {_acrossBest:F0} m"
                    : "already at the open-ground destination");
            BeginLeg();
            State = Mode.Striding;
            return true;
        }

        internal delegate bool ChordProbe(Vector3 from, Vector3 to);
        internal delegate bool ConnectorPlanner(Vector3 from, Vector3 to,
            List<Vector3> into, bool keepOffRoad);

        /// <summary>Join a leader's corridor from this member's actual start, follow all
        /// of it (including the common arrival point), and only then peel off to his own
        /// formation slot. Every chord is proved before it is accepted; a blocked join
        /// receives its own A* connector. The probes are parameters so this contract can
        /// be tested without a live Unity scene.</summary>
        internal static bool CopySharedWayModel(IReadOnlyList<Vector3> sharedWay,
            Vector3 from, Vector3 memberEnd, List<Vector3> into,
            List<Vector3> scratch, bool keepOffRoad, ChordProbe clear,
            ConnectorPlanner plan) =>
            CopySharedWayModel(sharedWay, 0, from, memberEnd, into, scratch,
                keepOffRoad, clear, plan);

        /// <summary>The same corridor contract resumed after the common corners before
        /// <paramref name="sharedFrom"/> have already been passed. A stalled member may
        /// plan only a connector to the next common corner, never a new whole trip to
        /// his private endpoint.</summary>
        internal static bool CopySharedWayModel(IReadOnlyList<Vector3> sharedWay,
            int sharedFrom, Vector3 from, Vector3 memberEnd, List<Vector3> into,
            List<Vector3> scratch, bool keepOffRoad, ChordProbe clear,
            ConnectorPlanner plan)
        {
            if (into == null || scratch == null || clear == null || plan == null)
                return false;
            into.Clear();
            scratch.Clear();
            const float same = 0.05f * 0.05f;
            from.y = memberEnd.y;
            if ((memberEnd - from).sqrMagnitude <= same)
                return true;
            var at = from;
            bool joined = false;
            int sharedCount = sharedWay != null ? sharedWay.Count : 0;
            // The group was given one route, so every member joins its beginning and
            // retains every corner. Letting each man skip to his own latest-visible
            // corner made one order split around both sides of the same block.
            for (int i = Mathf.Clamp(sharedFrom, 0, sharedCount); i < sharedCount; i++)
            {
                var corner = sharedWay[i];
                corner.y = memberEnd.y;
                if ((corner - at).sqrMagnitude <= same) continue;
                if (!AppendSafeConnector(at, corner, into, scratch,
                        keepOffRoad && !joined, keepOffRoad, clear, plan))
                {
                    into.Clear();
                    scratch.Clear();
                    return false;
                }
                at = corner;
                joined = true;
            }

            if ((memberEnd - at).sqrMagnitude > same &&
                !AppendSafeConnector(at, memberEnd, into, scratch,
                    keepOffRoad, keepOffRoad, clear, plan))
            {
                into.Clear();
                scratch.Clear();
                return false;
            }
            scratch.Clear();
            return true;
        }

        static bool AppendSafeConnector(Vector3 from, Vector3 to,
            List<Vector3> into, List<Vector3> scratch, bool enforceRoadPolicy,
            bool keepOffRoad, ChordProbe clear, ConnectorPlanner plan)
        {
            const float same = 0.05f * 0.05f;
            // keepOffRoad is more than collision clearance: WalkRoute also prices a
            // chord that runs along the carriageway. The member-only join and peel-off
            // therefore go through the planner even when statically clear. Shared
            // interior chords already carry the leader's road policy and are replanned
            // only if a new static obstacle has actually blocked one.
            if (!enforceRoadPolicy && clear(from, to))
            {
                AddRoutePoint(into, to, same);
                return true;
            }

            scratch.Clear();
            if (!plan(from, to, scratch, keepOffRoad) || scratch.Count == 0)
                return false;

            var at = from;
            for (int i = 0; i < scratch.Count; i++)
            {
                var point = scratch[i];
                point.y = to.y;
                if ((point - at).sqrMagnitude <= same) continue;
                // Do not trust a nominally successful plan blindly: its endpoint may
                // have been substituted after the lattice search, and this is exactly
                // the boundary where an unsafe direct chord used to enter the route.
                if (!clear(at, point)) return false;
                AddRoutePoint(into, point, same);
                at = point;
            }
            if ((to - at).sqrMagnitude > same)
            {
                if (!clear(at, to)) return false;
                AddRoutePoint(into, to, same);
            }
            return true;
        }

        static void AddRoutePoint(List<Vector3> into, Vector3 point, float same)
        {
            if (into.Count == 0 || (point - into[into.Count - 1]).sqrMagnitude > same)
                into.Add(point);
        }

        static bool StaticChordClear(Vector3 from, Vector3 to)
            => WalkRoute.ChordClear(from, to);

        void RememberSharedCorridor(IReadOnlyList<Vector3> sharedWay, float y)
        {
            ClearSharedCorridor();
            if (sharedWay == null) return;
            const float same = 0.05f * 0.05f;
            for (int i = 0; i < sharedWay.Count; i++)
            {
                var point = sharedWay[i];
                point.y = y;
                if (_sharedCorridor.Count == 0 ||
                    (point - _sharedCorridor[_sharedCorridor.Count - 1]).sqrMagnitude > same)
                    _sharedCorridor.Add(point);
            }
            _sharedAt = SharedCursorAfter(_sharedCorridor, 0, Tf.position);
        }

        void ClearSharedCorridor()
        {
            _sharedCorridor.Clear();
            _sharedAt = 0;
        }

        /// <summary>Advance only over the exact common corner just reached. Visibility
        /// to a later corner is intentionally irrelevant: it was that shortcut which
        /// let different members select opposite sides of a block.</summary>
        internal static int SharedCursorAfter(IReadOnlyList<Vector3> sharedWay,
            int sharedAt, Vector3 reached)
        {
            const float same = 0.05f * 0.05f;
            int count = sharedWay != null ? sharedWay.Count : 0;
            int at = Mathf.Clamp(sharedAt, 0, count);
            for (; at < count; at++)
            {
                var gap = sharedWay[at] - reached;
                gap.y = 0f;
                if (gap.sqrMagnitude > same) break;
            }
            return at;
        }

        void MarkSharedCornerReached(Vector3 reached) =>
            _sharedAt = SharedCursorAfter(_sharedCorridor, _sharedAt, reached);

        bool BuildRemainingWay(Vector3 from)
        {
            _sharedAt = SharedCursorAfter(_sharedCorridor, _sharedAt, from);
            if (_sharedAt < _sharedCorridor.Count)
                return CopySharedWayModel(_sharedCorridor, _sharedAt, from, _legEnd,
                    _legs, _connectorLegs, _legsOffRoad, StaticChordClear,
                    WalkRoute.Plan);
            return BuildIndividualWay(from, _legEnd, _legsOffRoad);
        }

        bool BuildIndividualWay(Vector3 from, Vector3 to, bool keepOffRoad)
        {
            // With no shared interior, the same connector contract is the complete
            // route. A clear chord stays cheap; a blocked one must come back from A*.
            return CopySharedWayModel(null, from, to, _legs, _connectorLegs,
                keepOffRoad, StaticChordClear, WalkRoute.Plan);
        }

        void RefuseUnsafeAcross()
        {
            _legs.Clear();
            _connectorLegs.Clear();
            ClearSharedCorridor();
            _pendingAcrossPlan = false;
            _legAt = 0;
            _legsVersion = WalkObstacles.Version;
            _legTo = _legEnd = Tf.position;
            _hold = 0f;
            _preferredSteerSide = 0;
            // A deferred order can still own the valid link it was standing on. Drop
            // only its old graph destination; retaining the seat lets a later graph
            // order reseat/route normally and makes OrderDestination the current spot.
            _route = null;
            _destFwd = _destBack = null;
            _destT = _targetT = 0f;
            Waiting = false;
            BeginLeg();
            State = Mode.Standing;
        }

        /// <summary>A direct cross-city order has no current or destination sidewalk.
        /// Clear both explicitly: retaining the last link made a striding man look as if
        /// he were still graph-driven, so later formation code sent him back to the
        /// pavement where the stride began. DemoCrews.Reseat can attach him to the
        /// nearest real link before a future graph order.</summary>
        void LeaveGraphOrder()
        {
            // Every fresh order starts a man who walks by the walls again. The doorway
            // order re-arms it immediately afterwards; nothing else can. Same for the
            // posted heading: a man sent somewhere else is off that door.
            _throughWall = false;
            _watching = false;
            _link = null;
            _cameFrom = null;
            _t = 0f;
            _route = null;
            _destFwd = _destBack = null;
            _destT = _targetT = 0f;
            Waiting = false;
        }

        // Original straight-line distance, retained only for the order trace. Progress
        // is judged on the current proved leg; distance to the final mark is not
        // monotone while a correct route goes round a building.
        float _acrossBest;

        /// <summary>Whether this walk is keeping off the carriageway - held for the
        /// replans, which must be drawn under the same rule as the first way or the man
        /// steps onto the road at his first corner.</summary>
        bool _legsOffRoad;

        /// <summary>The next corner, when there is one. Reached one, he goes on to the
        /// next; STOPPED SHORT of one, the way is drawn again from where he stands -
        /// it was drawn before he set off and the street has moved since. A few of
        /// those in a row and he is genuinely walled in, and he stands.</summary>
        bool NextLeg(bool arrived)
        {
            if (arrived)
            {
                _replans = 0;
                MarkSharedCornerReached(_legTo);
                if (++_legAt < _legs.Count) { _legTo = _legs[_legAt]; BeginLeg(); return true; }
                _legs.Clear();
                ClearSharedCorridor();
                return false;
            }
            if (_legs.Count == 0 || _replans >= 3) { _legs.Clear(); return false; }
            _replans++;
            bool rebuilt = BuildRemainingWay(Tf.position) && _legs.Count > 0;
            if (!rebuilt && TryRecoverRouteStart(_legEnd, "route replan"))
                rebuilt = BuildRemainingWay(Tf.position) && _legs.Count > 0;
            if (!rebuilt)
            { _legs.Clear(); return false; }
            _legAt = 0;
            _legTo = _legs[0];
            _legsVersion = WalkObstacles.Version;
            BeginLeg();
            return true;
        }

        bool RouteRemainderClear()
        {
            var at = Tf.position;
            for (int i = _legAt; i < _legs.Count; i++)
            {
                if (!StaticChordClear(at, _legs[i])) return false;
                at = _legs[i];
            }
            return true;
        }

        internal static float CornerStopDistance(bool last, bool nextChordClear) =>
            last ? 0.15f : nextChordClear ? 0.5f : 0.04f;

        internal static bool CornerReachedModel(float left, bool last,
                                                bool nextChordClear) =>
            left <= CornerStopDistance(last, nextChordClear);

        /// <summary>Exceptional recovery for geometry which appeared around a man, or
        /// an old numerical overlap. Relocate once to the nearest genuinely clear spot
        /// and redraw the whole remaining route; ordinary steering never receives a
        /// licence to walk through the containing wall.</summary>
        bool RecoverFixedOverlap()
        {
            const float MaxRecoveryStep = 2.5f;
            if (Crossing || !WalkObstacles.Standing(
                    Tf.position, WalkObstacles.OverlapProbeRadius))
                return false;
            var from = Tf.position;
            var toward = _sharedAt < _sharedCorridor.Count
                ? _sharedCorridor[_sharedAt]
                : _legEnd;
            if (!WalkObstacles.TryClearStandingSpot(
                    from, WalkRoute.ClearanceRadius, toward,
                    out var free, MaxRecoveryStep))
            {
                RefuseUnsafeAcross();
                return true;
            }
            free.y = from.y;
            Tf.position = free;
            _replans = 0;
            if (!BuildRemainingWay(free) || _legs.Count == 0)
            {
                RefuseUnsafeAcross();
                return true;
            }
            _legAt = 0;
            _legTo = _legs[0];
            _legsVersion = WalkObstacles.Version;
            BeginLeg();
            if (DriveTrace.On)
                DriveTrace.Event("walk", DisplayName,
                    $"recovered {Vector3.Distance(from, free):F1} m from fixed geometry");
            return true;
        }

        /// <summary>Close on this man and shoot him. Nothing happens unarmed.</summary>
        public void Engage(CrewWalker target)
        {
            if (Dead || Riding || !Carrying || Panicked || target == null || target.Dead || target == this) return;
            ClearFallingIn();
            if (Target != target)
            {
                _coverLooked = false;
                _underFire = 0;
                _coverRecheckAt = 0f;
                _coverSpot = null;
                InCover = false;
                _wasClosing = false;
                ClearCombatWay();
            }
            Target = target;
            EndChat();
            _watching = false;   // a doorman with a gun out is not on the door any more
            _blockedFor = 0f;
            _steerSide = 0;
            _preferredSteerSide = 0;
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

        /// <summary>Put his gun on a car and walk him to it. The tin does not have to
        /// be empty - a car with men in it is shot up just as well - but nothing about
        /// this is aimed at the men: the rounds go into the machine (DemoCrews.Resolve).</summary>
        public void ShootUp(CrewCar car)
        {
            if (Dead || Riding || !Carrying || Panicked) return;
            if (car == null || car.Tf == null || car.Wrecked) return;
            ClearFallingIn();
            DrawGun();
            Target = null;            // clears any car mark too, and then we set ours
            CarMark = car;
            EndChat();
            _blockedFor = 0f;
            _steerSide = 0;
            _preferredSteerSide = 0;
            _strideDir = Vector3.zero;
            _runningLeg = false;
            _sprinting = false;
            _keepingLow = false;
            _coverSpot = null;
            InCover = false;
            SetPace(1f);
            State = Mode.Engaging;
            if (_fireTimer <= 0f)
                _fireTimer = Ballistics.Interval * Random.Range(0.4f, 1f);
        }

        /// <summary>Metres a second at which a car counts as driving off rather than
        /// standing: past this nobody walks after it.</summary>
        const float DrivingOff = 2.5f;

        /// <summary>Where on a machine a man puts his rounds: the middle of it at about
        /// the height of a door. NOT its pivot - a car's origin sits on the road between
        /// its wheels, and a crew aiming at that fires into the tarmac.</summary>
        public static Vector3 CarAim(CrewCar car) =>
            car == null || car.Tf == null ? Vector3.zero : car.Tf.position + Vector3.up * 0.9f;

        /// <summary>Lower the gun and stand.</summary>
        public void Disengage()
        {
            if (Dead) return;
            ClearFallingIn();
            Target = null;
            ClearCombatWay();
            _coverSpot = null;
            InCover = false;
            if (State == Mode.Engaging) State = Mode.Standing;
        }

        // ------------------------------------------------------------------ arms

        /// <summary>Give him this gun (replacing whatever he carried); null disarms.
        ///
        /// THIS DOES NOT PUT ANYTHING IN HIS HAND. It writes what he carries, which is
        /// the ledger's business, and the street decides on its own when the piece comes
        /// out from under the coat (TickArms). A man dealt a rifle by the book at the
        /// outfit's door walks off with it out of sight - the alternative is what the
        /// player watched for months: five men strolling a shopping street with
        /// machine guns in their fists and not a policeman in the town minding it.
        ///
        /// The one exception is a swap made while the gun is ALREADY out - the saddle's
        /// cap trading a rifle for a machine pistol mid-pass (CrewBike.CapArms) - which
        /// puts the new piece straight back in the fist it took the old one from.</summary>
        public void Arm(GameObject prefab, EquipmentKind kind)
        {
            bool wasOut = Armed;
            Holster();
            WeaponPrefab = prefab;
            WeaponKind = kind;
            var ballistics = CrewArms.StatsFor(kind);
            // CoverDemo's authored rifle is the automatic rifle shown by the Mixamo
            // machine-gun take. Keep this inside the opt-in wardrobe: a rifle in the
            // live city retains the deliberate rifle stats above. Range and report
            // stay rifle-like; cadence, per-round damage and spread become automatic.
            if (AuthoredLongGunWardrobe && kind == EquipmentKind.Rifle &&
                HasPose(PoseAutomaticShoot))
            {
                var automatic = CrewArms.StatsFor(EquipmentKind.TommyGun);
                ballistics.Interval = automatic.Interval;
                ballistics.Damage = automatic.Damage;
                ballistics.Accuracy = automatic.Accuracy;
            }
            Ballistics = ballistics;
            // a swap made on a man who is ON HIS WAY DOWN still goes into the hand:
            // he is shot off a pillion holding the saddle's machine pistol, the
            // dismount hands him his own gun back, and what has to fall out of his
            // fist a moment later is the gun the books say he owned.
            if (wasOut) IntoTheHand();
        }

        /// <summary>Out from under the coat and into his fist, and KEPT there: every
        /// call pushes the quiet timer back out to full, so a caller that wants the gun
        /// out for a while (a pillion on the run-in to a pass) just says so every frame
        /// and never has to think about the timer. Idempotent, and cheap enough to be
        /// called from a frame tick - a man who already has it out only gets the
        /// push.</summary>
        public void DrawGun()
        {
            if (Dead) return;
            _armsQuiet = ArmsQuiet;
            if (Armed) return;
            IntoTheHand();
        }

        void IntoTheHand()
        {
            if (WeaponPrefab == null || Tf == null) return;
            var animator = Tf.GetComponentInChildren<Animator>();
            _armsAnimator = animator;
            Weapon = CrewArms.Attach(animator, WeaponPrefab);
            _aimArm = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
            _aimForearm = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightLowerArm) : null;
            _aimHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            _supportArm = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
            _supportForearm = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftLowerArm) : null;
            _supportHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            if (animator != null && _supportHand != null)
            {
                var inv = Quaternion.Inverse(CrewArms.TPoseRotation(animator, _supportHand));
                _supportFingers = inv * Vector3.left;
                _supportThumb = inv * Vector3.forward;
            }
            _longGunGripBlend = 0f;
            _idleGripBlend = 0f;
            _idleGripKnown = false;
            if (Weapon != null) _weaponGripBase = Weapon.localPosition;
        }

        /// <summary>Away it goes; he still carries it. The model is destroyed rather
        /// than hidden because that is the same handful of objects the deal already
        /// makes and unmakes every time the book changes a man's piece, and a gun
        /// parked inside a coat is a renderer the city pays for on every one of a
        /// thousand men.
        ///
        /// A DEAD MAN IS NEVER HOLSTERED. His gun leaves the hand the other way, by
        /// falling out of it (DropGun) - it lies in the road as a prop, and destroying
        /// it here would take that prop with it.</summary>
        public void Holster()
        {
            if (Weapon == null)
            {
                _armsAnimator = null;
                return;
            }
            Object.Destroy(Weapon.gameObject);
            Weapon = null;
            _armsAnimator = null;
            _aimArm = null;
            _aimForearm = null;
            _aimHand = null;
            _supportArm = null;
            _supportForearm = null;
            _supportHand = null;
            _aimBlend = 0f;
            _longGunGripBlend = 0f;
            _idleGripBlend = 0f;
            _idleGripKnown = false;
        }

        /// <summary>How long the gun stays out after the last reason for it has gone.
        /// A fight is not over when the man in front of you falls - the one behind him
        /// is still somewhere - and a piece that goes back under the coat the instant a
        /// mark drops reads as a man who was never worried. Long enough to cover the
        /// gap between one exchange and the next, short enough that a street settles.</summary>
        const float ArmsQuiet = 8f;

        /// <summary>Metres before his gun's reach at which a man draws on the way into
        /// a fight. At the crew's fight jog this is about a second's warning: enough
        /// time for the piece to appear and the arm to rise, but not a whole walk across
        /// the quarter with a rifle already in his fist.</summary>
        const float DrawBeforeRange = 3f;

        float _armsQuiet;

        /// <summary>Has the approach reached the point where this fight needs a gun in
        /// hand? A man on a moving machine is never closed on, so its passing-shot reach
        /// is his effective range; everybody else uses the piece's own reach.</summary>
        bool FightCloseEnoughToDraw
        {
            get
            {
                if (State != Mode.Engaging || Target == null || !Target.Tf || Target.Dead || Tf == null)
                    return false;
                float range = Target.Riding || Target.Astride
                    ? Mathf.Max(Ballistics.Range * 1.35f, PassingShot)
                    : Ballistics.Range;
                var to = Target.Tf.position - Tf.position;
                to.y = 0f;
                float draw = range + DrawBeforeRange;
                return to.sqrMagnitude <= draw * draw;
            }
        }

        /// <summary>Is there anything for a gun to be out FOR? This is the whole of the
        /// concealment rule, and it is deliberately about the moment rather than the
        /// man: nothing here asks who he is or what he was dealt, only whether the
        /// street he is stood on has turned into one where a piece is drawn.
        ///
        /// Riding is on the list twice over - a man at a wound-down window on a pass,
        /// and the pillion on a saddle - because both are RidingAim, which is set by
        /// whoever is driving the machine the moment it has a mark on its flank.</summary>
        public bool WantsGunOut =>
            !Dead && Carrying && !Surrendered &&
            (FightCloseEnoughToDraw ||  // a fight, once he is about to enter its reach
             CarMark != null ||         // a car he was explicitly told to shoot up
             State == Mode.Fleeing ||    // running from one, which is still one
             Alert ||                    // shooting within earshot, twelve seconds of it
             RidingAim ||                // out of a window, or off the back of a machine
             _shoutLeft > 0f);           // the law's warning, shouted with the gun up

        /// <summary>The piece comes out when the street calls for it and goes away when
        /// the street has been quiet a while. Run before anything else in the frame so
        /// that every branch below - the fight, the ride, the pose picker - sees the
        /// hand it is going to be posing.</summary>
        void TickArms(float dt)
        {
            // a man in the air off a motorcycle keeps whatever he had: the spill owns
            // his body for the length of it, and a gun that vanishes mid-tumble reads
            // as the gun being what threw him
            if (Dead || Spilling) return;
            if (WantsGunOut) { DrawGun(); return; }
            if (!Armed) return;
            _armsQuiet -= dt;
            if (_armsQuiet <= 0f) Holster();
        }

        // ------------------------------------------------------------------ the aim

        // The gun arm, turned at the shoulder after the animation. The pistol clips
        // aim where they were authored to aim - at the horizon of the rig they were
        // made on - and on the pack bodies that lands the barrel in the pavement a
        // few strides out. The clip cannot know where the other man stands; this does.
        Transform _aimArm, _aimForearm, _aimHand;
        Animator _armsAnimator;
        float _aimBlend;
        Vector3 _aimDir;
        Transform _supportArm, _supportForearm, _supportHand;
        Vector3 _supportFingers, _supportThumb;
        Vector3 _weaponGripBase, _idleGripOffset;
        float _idleGripBlend;
        bool _idleGripKnown;
        float _longGunGripBlend;

        /// <summary>The Mixamo rifle wardrobe is deliberately opt-in. CoverDemo is
        /// the only builder that supplies it; every live-city walker stays on the old
        /// procedural arm solve even though both paths share this class.</summary>
        bool UsesAuthoredLongGun =>
            AuthoredLongGunWardrobe && CrewArms.TwoHanded(WeaponKind);

        bool UsesAuthoredSidearm =>
            AuthoredSidearmWardrobe && CrewArms.IsFirearm(WeaponKind) &&
            !CrewArms.TwoHanded(WeaponKind);

        // The prop in the hand is the switch. Before DrawGun succeeds this man uses
        // the ordinary Synty movement wardrobe; after it succeeds the rifle gait may
        // be shown, but all pace and routing decisions still read the Synty slots.
        bool ShowsAuthoredLongGun => Armed && UsesAuthoredLongGun;
        bool ShowsAuthoredSidearm => Armed && UsesAuthoredSidearm;
        int VisibleWalkPose => ShowsAuthoredLongGun && HasPose(PoseRifleWalk)
            ? PoseRifleWalk : PoseWalk;
        int VisibleJogPose => ShowsAuthoredLongGun && HasPose(PoseRifleJog)
            ? PoseRifleJog : PoseJog;
        int VisibleSprintPose => ShowsAuthoredLongGun && HasPose(PoseRifleSprint)
            ? PoseRifleSprint : PoseSprint;
        int VisibleCrouchWalkPose => ShowsAuthoredLongGun && HasPose(PoseRifleCrouchWalk)
            ? PoseRifleCrouchWalk : PoseCrouchWalk;

        int VisibleArmedIdlePose => ShowsAuthoredLongGun && HasPose(PoseRifleIdle)
            ? PoseRifleIdle
            : Armed && HasPose(PosePistolIdle) ? PosePistolIdle : PoseIdle;

        int VisibleAimPose => ShowsAuthoredLongGun && HasPose(PoseRifleAim)
            ? PoseRifleAim : HasPose(PoseAim) ? PoseAim : VisibleArmedIdlePose;

        int VisibleCrouchPose => ShowsAuthoredLongGun && HasPose(PoseRifleCrouch)
            ? PoseRifleCrouch
            : ShowsAuthoredSidearm && HasPose(PosePistolCrouch)
                ? PosePistolCrouch
                : HasPose(PoseCrouch) ? PoseCrouch : VisibleArmedIdlePose;

        void GearVisibleRifleGait(int pose, float pace, float fallback, float jitter = 1f)
        {
            if (pose != PoseRifleWalk && pose != PoseRifleJog &&
                pose != PoseRifleSprint && pose != PoseRifleCrouchWalk) return;
            SetPoseSpeed(pose,
                Mathf.Clamp(pace / ClipPace(pose, fallback), 0.45f, 1.5f) * jitter);
        }

        bool TryWeaponGait(bool jog, bool sprint, Vector3 travel, float pace, out int pose)
        {
            pose = -1;
            AnimationClip[] set = null;
            AnimationClip fallback = null;
            float natural = jog ? (sprint ? SprintClipPace : JogClipPace) : WalkClipPace;

            if (ShowsAuthoredLongGun)
            {
                if (_keepingLow)
                {
                    set = RifleCrouchWalkGaits;
                    fallback = ForwardGait(RifleCrouchWalkGaits);
                    natural = 1.3f;
                }
                else if (sprint)
                {
                    set = RifleSprintGaits;
                    fallback = ForwardGait(RifleSprintGaits);
                }
                else if (jog)
                {
                    set = RifleRunGaits;
                    fallback = ForwardGait(RifleRunGaits);
                }
                else
                {
                    set = RifleWalkGaits;
                    fallback = ForwardGait(RifleWalkGaits);
                }
            }
            else if (ShowsAuthoredSidearm && !_keepingLow)
            {
                set = jog ? PistolRunGaits : PistolWalkGaits;
                fallback = ForwardGait(set);
            }
            if (set == null || set.Length < 8) return false;

            var step = WeaponStepFor(travel);
            var clip = set[(int)step] ?? fallback;
            if (clip == null) return false;
            pose = DirectionalGaitPose(clip, jog, pace, natural,
                jog ? _runJitter : WalkCadence);
            return pose >= 0;
        }

        static AnimationClip ForwardGait(AnimationClip[] set) =>
            set != null && set.Length > (int)RifleStep.Forward
                ? set[(int)RifleStep.Forward] : null;

        RifleStep _weaponStep;
        bool _weaponStepKnown;

        RifleStep WeaponStepFor(Vector3 worldTravel)
        {
            worldTravel.y = 0f;
            if (worldTravel.sqrMagnitude < 1e-5f)
                return _weaponStepKnown ? _weaponStep : RifleStep.Forward;

            var local = Tf.InverseTransformDirection(worldTravel.normalized);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            if (_weaponStepKnown && Mathf.Abs(Mathf.DeltaAngle(
                    StepAngle(_weaponStep), angle)) <= 30f)
                return _weaponStep;

            _weaponStep = StepAt(angle);
            _weaponStepKnown = true;
            return _weaponStep;
        }

        static RifleStep StepAt(float angle)
        {
            int sector = Mathf.RoundToInt(Mathf.DeltaAngle(0f, angle) / 45f);
            return sector switch
            {
                0 => RifleStep.Forward,
                1 => RifleStep.ForwardRight,
                2 => RifleStep.Right,
                3 => RifleStep.BackwardRight,
                4 or -4 => RifleStep.Backward,
                -3 => RifleStep.BackwardLeft,
                -2 => RifleStep.Left,
                _ => RifleStep.ForwardLeft,
            };
        }

        static float StepAngle(RifleStep step) => step switch
        {
            RifleStep.Forward => 0f,
            RifleStep.ForwardRight => 45f,
            RifleStep.Right => 90f,
            RifleStep.BackwardRight => 135f,
            RifleStep.Backward => 180f,
            RifleStep.BackwardLeft => -135f,
            RifleStep.Left => -90f,
            _ => -45f,
        };

        bool TryTacticalFacing(out Vector3 toward)
        {
            toward = Vector3.zero;
            if (!Armed || State != Mode.Engaging ||
                (!ShowsAuthoredLongGun && !ShowsAuthoredSidearm)) return false;
            if (Target != null && Target.Tf && !Target.Dead)
                toward = Target.Tf.position - Tf.position;
            else if (CarMark != null && CarMark.Tf != null && !CarMark.Wrecked)
                toward = CarMark.Tf.position - Tf.position;
            toward.y = 0f;
            return toward.sqrMagnitude > 1e-4f;
        }

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
            bool onCar = Target == null && CarMark != null && CarMark.Tf != null && !CarMark.Wrecked;
            // The old crowd run needs its procedural rifle overlay suppressed. The
            // authored rifle run already owns both arms, so only its prop is corrected.
            bool carryingLongGunAtRun = _runningLeg && CrewArms.TwoHanded(WeaponKind) &&
                                        !UsesAuthoredLongGun;
            bool aiming = !Dead && Armed && State == Mode.Engaging && _flinch <= 0f &&
                          !carryingLongGunAtRun &&
                          (onCar || (Target != null && Target.Tf && !Target.Dead)) &&
                          !(InCover && _ducked);
            // what the arm is turned at: a man's chest, or the flank of a machine
            var markAt = onCar ? CarMark.Tf.position : (Target != null && Target.Tf ? Target.Tf.position : Tf.position);
            // his Tf is asked for twice over on purpose: a CrewWalker is a plain object
            // and outlives the body it drives, so a man who was removed from the scene
            // mid-fight (a deserter struck off the roster) is a Target that is NOT null
            // with a Transform that has been destroyed. Reading his chest through it threw
            // out of LateUpdate every frame for the rest of the run.
            var markAim = onCar ? CarAim(CarMark)
                                : (Target != null && Target.Tf ? Target.ChestPosition : Tf.position);
            if (aiming)
            {
                var flat = markAt - Tf.position;
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
                float reach = Target != null && (Target.Riding || Target.Astride)
                    ? Mathf.Max(Ballistics.Range * 1.35f, PassingShot)
                    : Ballistics.Range * 1.35f;
                aiming = flat.magnitude <= reach &&
                         CombatAimError(flat) < 70f &&
                         StrideAllowsAim(flat);
            }
            _aimBlend = Mathf.MoveTowards(_aimBlend, aiming ? 1f : 0f, 6f * dt);
            if (UsesAuthoredLongGun)
            {
                ApplyAuthoredIdleGrip(dt);
                AimAuthoredLongGun(aiming, markAim);
                _longGunGripBlend = 0f;
                return;
            }
            if (CrewArms.TwoHanded(WeaponKind))
                PoseLongGunFire(aiming ? markAim : MuzzlePosition + _aimDir * 10f,
                    _aimBlend);
            else
                AimRightArm(aiming, markAim);
            if (!aiming && CurrentPose == PosePistolIdle)
                PoseGunLow(1f - _aimBlend);
            PoseLongGun(dt);
        }

        /// <summary>The rifle take already places both arms. Moving either bone again
        /// destroys that authored hold, so the only correction is the prop: its bore is
        /// laid exactly from the muzzle to the mark while its trigger hand stays put.</summary>
        void AimAuthoredLongGun(bool aiming, Vector3 markAim)
        {
            if (!aiming || Weapon == null || _armsAnimator == null) return;
            var muzzle = CrewArms.MuzzleOf(Weapon);
            if (muzzle == null) return;
            // Rotating around the trigger fist moves the muzzle slightly. The second
            // pass removes that small parallax, as on the rifle review bench.
            for (int pass = 0; pass < 2; pass++)
            {
                var aim = markAim - muzzle.position;
                if (aim.sqrMagnitude < 0.04f) return;
                CrewArms.FitToAim(_armsAnimator, Weapon, aim.normalized, Vector3.up);
            }
            TurnAuthoredHeadTo(markAim);
        }

        /// <summary>The rifle bench's idle-only three-centimetre grip correction,
        /// applied to the production walker too. It advances the handle from the wrist
        /// into this avatar's fist and blends completely out for every other pose.</summary>
        void ApplyAuthoredIdleGrip(float dt)
        {
            if (Weapon == null || _armsAnimator == null || Weapon.parent == null) return;
            if (!_idleGripKnown)
            {
                var wrist = _armsAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                if (wrist != null)
                {
                    var toward = CrewArms.GripPoint(_armsAnimator, false) - wrist.position;
                    if (toward.sqrMagnitude > 1e-6f)
                    {
                        _idleGripOffset = Weapon.parent.InverseTransformDirection(
                            toward.normalized) * 0.03f;
                        _idleGripKnown = true;
                    }
                }
            }
            float wanted = CurrentPose == PoseRifleIdle && _idleGripKnown ? 1f : 0f;
            _idleGripBlend = Mathf.MoveTowards(
                _idleGripBlend, wanted, 9f * Mathf.Max(0f, dt));
            Weapon.localPosition = _weaponGripBase + _idleGripOffset * _idleGripBlend;
        }

        /// <summary>Only sustained aiming states turn the skull. Shot and gunplay
        /// takes retain their authored head movement, as do idle, turn, jump and fall.</summary>
        void TurnAuthoredHeadTo(Vector3 markAim)
        {
            bool tracks = CurrentPose == PoseRifleAim ||
                          CurrentPose == PoseRifleWalk || CurrentPose == PoseRifleJog ||
                          CurrentPose == PoseRifleSprint ||
                          CurrentPose == PoseRifleCrouchWalk ||
                          CurrentPose == PoseWeaponGaitA || CurrentPose == PoseWeaponGaitB;
            if (!tracks || _armsAnimator == null) return;
            var head = _armsAnimator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            var wanted = markAim - head.position;
            if (wanted.sqrMagnitude < 0.04f) return;
            var look = CrewArms.LookDirection(_armsAnimator);
            if (look.sqrMagnitude < 1e-4f) return;
            var turn = Quaternion.FromToRotation(look, wanted.normalized);
            head.rotation = Quaternion.RotateTowards(
                head.rotation, turn * head.rotation, 32f);
        }

        /// <summary>Desired fighting yaw. Ordinary crews still square their chest at
        /// the mark. An opted-in rifle take is turned by its two-hand axis instead,
        /// producing the authored bladed stance without rotating any limb.</summary>
        Quaternion CombatAimRotation(Vector3 toward)
        {
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-5f) return Tf.rotation;
            float bearing = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            // The tactical wardrobe is directional: forward, arcs, strafes and
            // backwards steps all describe travel relative to a body whose two-hand
            // axis stays on the mark. Therefore the same bladed solve is valid while
            // moving as while standing; forcing body-forward onto travel here would
            // undo the directional gait and point the rifle away from its target.
            if (UsesAuthoredLongGun && _armsAnimator != null)
            {
                var hands = CrewArms.HandAimAxis(_armsAnimator);
                hands.y = 0f;
                if (hands.sqrMagnitude > 0.01f)
                {
                    var local = Quaternion.Inverse(Tf.rotation) * hands.normalized;
                    bearing -= Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                }
            }
            return Quaternion.Euler(0f, bearing, 0f);
        }

        void TurnCombat(Vector3 toward, float degreesPerSecond, float dt)
        {
            if (toward.sqrMagnitude < 1e-5f) return;
            Tf.rotation = Quaternion.RotateTowards(
                Tf.rotation, CombatAimRotation(toward), degreesPerSecond * dt);
        }

        /// <summary>The old trigger gate judges chest-forward. A bladed rifleman must
        /// instead be judged by the line between his hands or he can never legally fire.</summary>
        float CombatAimError(Vector3 toward)
        {
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-5f) return 0f;
            if (UsesAuthoredLongGun && _armsAnimator != null)
            {
                var hands = CrewArms.HandAimAxis(_armsAnimator);
                hands.y = 0f;
                if (hands.sqrMagnitude > 0.01f)
                    return Vector3.Angle(hands, toward);
            }
            return Vector3.Angle(Tf.forward, toward);
        }

        void AimRightArm(bool aiming, Vector3 markAim)
        {
            if (_aimBlend <= 0.001f || _aimArm == null || Weapon == null) return;
            var muzzle = CrewArms.MuzzleOf(Weapon);
            var mp = muzzle != null ? muzzle.position : Weapon.position;
            var mf = muzzle != null ? muzzle.forward : Weapon.forward;
            if (aiming) _aimDir = (markAim - mp).normalized;
            if (_aimDir.sqrMagnitude < 1e-4f) return;
            var turn = Quaternion.FromToRotation(mf, _aimDir);
            turn.ToAngleAxis(out float angle, out var axis);
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return;
            if (angle > 180f) angle -= 360f;
            angle = Mathf.Clamp(angle, -70f, 70f) * _aimBlend;
            if (Mathf.Abs(angle) < 0.05f) return;
            _aimArm.rotation = Quaternion.AngleAxis(angle, axis) * _aimArm.rotation;
        }

        /// <summary>Shoulder a long gun after the pistol-authored aim or shoot clip has
        /// posed the body. The trigger hand is brought back to the chest, the elbow is
        /// kept outside the ribs, and the weapon is turned onto the mark before the
        /// support-hand solve runs. The Hood demo calls this same production pose.</summary>
        public void PoseLongGunFire(Vector3 aimAt, float weight)
        {
            weight = Mathf.Clamp01(weight);
            if (weight <= 0.001f || Tf == null || Weapon == null ||
                !CrewArms.TwoHanded(WeaponKind) || _aimArm == null ||
                _aimForearm == null || _aimHand == null) return;

            var muzzle = CrewArms.MuzzleOf(Weapon);
            if (muzzle == null) return;
            var aim = aimAt - muzzle.position;
            if (aim.sqrMagnitude < 1e-5f) aim = Tf.forward;
            aim.Normalize();
            var flat = Vector3.ProjectOnPlane(aim, Tf.up);
            if (flat.sqrMagnitude < 1e-5f) flat = Tf.forward;
            flat.Normalize();

            // A pistol clip leaves the trigger fist at full extension. A stock-fired
            // piece keeps that fist close to the right breast and lets the forearm and
            // shoulder carry the aim instead.
            var hold = _aimArm.position + flat * 0.24f -
                       Tf.right * 0.13f - Tf.up * 0.11f;
            var target = Vector3.Lerp(_aimHand.position, hold, weight);
            var pole = _aimArm.position + Tf.right * 0.42f -
                       Tf.up * 0.16f + flat * 0.12f;

            // Remember the hand turn before solving the arm: the gun is parented to
            // this bone, so this is also the exact turn that puts the barrel on aim.
            var handTurn = Quaternion.FromToRotation(muzzle.forward, aim) * _aimHand.rotation;
            BikePose.TwoBone(_aimArm, _aimForearm, _aimHand, target, pole);
            _aimHand.rotation = Quaternion.Slerp(_aimHand.rotation, handTurn, weight);
        }

        /// <summary>Turn the armed idle into an actual low-ready pose. The animation
        /// pack calls its clip Pistol_Idle, but authors both fists at presentation
        /// height; without this shared overlay every gun points ahead while the state
        /// and HUD say it is low. Sidearms settle by the right hip. A stock-fired gun
        /// stays against the chest, with its muzzle safely down and its left hand then
        /// solved onto the fore-end by <see cref="PoseLongGun"/>.</summary>
        public void PoseGunLow(float weight)
        {
            weight = Mathf.Clamp01(weight);
            if (weight <= 0.001f || Tf == null || Weapon == null || _aimArm == null ||
                _aimForearm == null || _aimHand == null) return;

            var muzzle = CrewArms.MuzzleOf(Weapon);
            if (muzzle == null) return;
            bool longGun = CrewArms.TwoHanded(WeaponKind);
            var low = (Tf.forward * (longGun ? 0.9f : 0.65f) -
                       Tf.up * (longGun ? 0.42f : 0.76f)).normalized;
            var hold = longGun
                ? _aimArm.position + Tf.forward * 0.18f - Tf.right * 0.1f - Tf.up * 0.18f
                : _aimArm.position + Tf.forward * 0.1f + Tf.right * 0.12f - Tf.up * 0.34f;
            var target = Vector3.Lerp(_aimHand.position, hold, weight);
            var pole = _aimArm.position + Tf.right * 0.42f -
                       Tf.up * 0.2f + Tf.forward * 0.08f;
            var handTurn = Quaternion.FromToRotation(muzzle.forward, low) * _aimHand.rotation;

            BikePose.TwoBone(_aimArm, _aimForearm, _aimHand, target, pole);
            _aimHand.rotation = Quaternion.Slerp(_aimHand.rotation, handTurn, weight);
        }

        /// <summary>Put the left hand on the actual fore-end of a long gun after the
        /// animation graph has posed the body. The authored pistol clips and the
        /// airport run overlay provide the broad shoulder pose; this shared two-bone
        /// solve provides the missing contract they cannot know: where this particular
        /// rifle, shotgun or Tommy gun sits in this particular rig's right fist.</summary>
        public void PoseLongGun(float dt)
        {
            bool holding = !Dead && !Spilling && Tf != null && Weapon != null &&
                           CrewArms.TwoHanded(WeaponKind);
            if (!holding)
            {
                _longGunGripBlend = 0f;
                return;
            }
            _longGunGripBlend = Mathf.MoveTowards(_longGunGripBlend,
                1f, Mathf.Max(0f, dt) * 10f);
            if (_longGunGripBlend <= 0.001f || _supportArm == null ||
                _supportForearm == null || _supportHand == null) return;

            var grip = CrewArms.SupportGripOf(Weapon);
            if (grip == null) return;

            // The marker is the middle of the fore-end. All three long-gun models put
            // it a shade too far towards the muzzle for this shared pose, so bring the
            // man's left wrist a few centimetres back towards the trigger.
            var wrist = grip.position - grip.forward * 0.035f -
                        grip.right * 0.045f - grip.up * 0.018f;
            var target = Vector3.Lerp(_supportHand.position, wrist, _longGunGripBlend);
            var pole = _supportArm.position - Tf.right * 0.38f - Tf.up * 0.22f +
                       Tf.forward * 0.12f;
            BikePose.TwoBone(_supportArm, _supportForearm, _supportHand, target, pole);

            var hand = Quaternion.LookRotation(grip.right, grip.forward) *
                       Quaternion.Inverse(Quaternion.LookRotation(
                           _supportFingers, _supportThumb));
            _supportHand.rotation = Quaternion.Slerp(
                _supportHand.rotation, hand, _longGunGripBlend);
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
            ClearFallingIn();
            Health = 0;
            Target = null;
            // shot in the seat: the window pose stops writing his arm at once, or it
            // would hold the gun out of the window over the top of him dying
            Seated(null);
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
            TickArms(dt);
            BlendLongGunRun(!Dead && !Spilling && Armed && _runningLeg &&
                            CrewArms.TwoHanded(WeaponKind), dt);
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
                        if (!_gunFallDone && !Spilling && at >= len * 0.45f) DropGun();
                        if (at >= len - 0.03f) HoldPose(PoseDeath);
                    }
                    else if (!_gunFallDone && !Spilling) DropGun();
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
                    if (RecoverFixedOverlap()) return;
                    if (_pendingAcrossPlan)
                    {
                        if (HoldingBeat(dt)) return;
                        _pendingAcrossPlan = false;
                        bool planned = BuildIndividualWay(
                            Tf.position, _legEnd, _legsOffRoad);
                        if (!planned && TryRecoverRouteStart(
                                _legEnd, "deferred route plan"))
                            planned = BuildIndividualWay(
                                Tf.position, _legEnd, _legsOffRoad);
                        if (!planned)
                        {
                            RefuseUnsafeAcross();
                            return;
                        }
                        _legAt = 0;
                        _legTo = _legs.Count > 0 ? _legs[0] : _legEnd;
                        _legsVersion = WalkObstacles.Version;
                        LeaveGraphOrder();
                        BeginLeg();
                    }
                    else if (HoldingBeat(dt)) return;

                    // A streamed/static ledger change invalidates only a route whose
                    // remaining chords it actually touched. Clear routes keep walking;
                    // stale ones are redrawn before live steering reaches the new wall.
                    if (_legsVersion != WalkObstacles.Version)
                    {
                        _legsVersion = WalkObstacles.Version;
                        if (!RouteRemainderClear())
                        {
                            if (NextLeg(false)) return;
                            RefuseUnsafeAcross();
                            return;
                        }
                    }

                    bool last = _legAt >= _legs.Count - 1;
                    bool earlyCorner = !last &&
                        StaticChordClear(Tf.position, _legs[_legAt + 1]);
                    float stopWithin = CornerStopDistance(last, earlyCorner);
                    var toTerminal = _legEnd - Tf.position;
                    toTerminal.y = 0f;
                    NoteRoutedStrideIntent(
                        _legEnd, toTerminal.magnitude > CornerStopDistance(true, false));
                    TickStride(dt, _legTo, stopWithin, hurry: Hustle, run: Running(),
                        terminal: last, routed: true);
                    var gap = _legTo - Tf.position;
                    gap.y = 0f;
                    float left = gap.magnitude;
                    // there, or as near as the street lets him: a spot another man is
                    // stood on, or a car is parked on, is not reached, it is stopped
                    // short of - no marching in place. A corner on the way somewhere
                    // else is not a spot to stand on either: near enough IS round it.
                    // Smooth early hand-off is allowed only when the ACTUAL current
                    // point sees the next corner. Otherwise close to the proved corner;
                    // the old unconditional 0.5 m cut is enough to cross an inflated
                    // building corner in a reproducible case.
                    bool nextClear = !last &&
                        StaticChordClear(Tf.position, _legs[_legAt + 1]);
                    bool there = CornerReachedModel(left, last, nextClear);
                    if (!last && !nextClear && left <= 0.04f)
                    {
                        if (NextLeg(false)) return;
                        RefuseUnsafeAcross();
                        return;
                    }

                    if (!there && !LegStalled(left, dt)) return;
                    if (NextLeg(there)) return;
                    if (there) State = Mode.Standing;
                    else RefuseUnsafeAcross();
                    return;
                }

                case Mode.Engaging:
                    TickEngage(dt);
                    return;

                case Mode.Riding:
                    // seated in the car (the arena carries him); gun out of the window
                    // when there is someone to shoot at, else just sitting.
                    //
                    // A SEATED MAN IS NEVER PLAYED A STANDING CLIP. The aim clip is a
                    // man stood up on his own two feet, and a seat is a root on the
                    // cushion with the pelvis carried up off it (CarBody's seats are
                    // measured for the sit loop and nothing else): played the aim clip
                    // he stands up where he sits, which put a head out of the roof of
                    // every car on every drive-by the town ever drove. So the sit loop
                    // keeps playing and the aim is DERIVED over the top of it, the lean
                    // and the gun arm both (SeatPose).
                    //
                    // On a bike none of that applies either: BikePose writes his arms,
                    // his legs and his spine every frame over whatever plays here, so
                    // what plays here only has to sit his pelvis down and keep him
                    // breathing.
                    SetPose(HasPose(PoseRide) && Astride ? PoseRide : HasPose(PoseSit) ? PoseSit : PoseIdle);
                    if (!Astride)
                        Seated(RidingAim && Target != null && Target.Tf != null && !Target.Dead
                            ? Target.ChestPosition : (Vector3?)null);
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

        // ------------------------------------------------------------------ the car

        /// <summary>In a car seat, carried by the car; takes no orders of his own.</summary>
        public bool Riding => State == Mode.Riding;

        /// <summary>While riding: gun up and out of the window (the drive-by).</summary>
        public bool RidingAim;

        /// <summary>What he is shooting at out of the window (or nothing) - the arena's
        /// call while he rides; the seat, not the man, decides what he can see.</summary>
        public void AimAt(CrewWalker mark)
        {
            ClearFallingIn();
            Target = mark != null && !mark.Dead ? mark : null;
        }

        /// <summary>Astride something rather than sat in it - a motorcycle. His legs
        /// stay where everyone can see them and BikePose puts them on the pegs.</summary>
        public bool Astride { get; private set; }

        // The lean and the gun arm of a man shooting out of a car window, laid over the
        // sit loop (SeatPose - and see the Riding case of TickCrew for why it is not a
        // clip). Hung on him the first time he puts a gun out of a window and kept: a
        // component that has read the rig once has nothing left to do on the frames he
        // is not firing, and taking it off and putting it back would read the rig again
        // every pass. A body it cannot pose is asked once and never again.
        SeatPose _seated;
        bool _seatless;

        void Seated(Vector3? aim)
        {
            if (_seated == null)
            {
                if (!aim.HasValue || _seatless || Tf == null) return;
                var pose = Tf.gameObject.AddComponent<SeatPose>();
                if (!pose.Setup(Tf.GetComponentInChildren<Animator>()))
                {
                    Object.Destroy(pose);
                    _seatless = true;
                    return;
                }
                _seated = pose;
            }
            _seated.AimAt = aim;
            _seated.enabled = aim.HasValue;
        }

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
            ClearFallingIn();
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
            ClearFallingIn();
            Astride = on && astride;
            // the legs go with the seat either way - a dead man is lifted out whole
            HideLegs(on && !astride);
            // out of the seat, out of the window pose: the arm goes back to the clip
            if (!on) Seated(null);
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
            SetPose(Armed ? VisibleArmedIdlePose : PoseIdle);
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
            SetPose(HasPose(PoseShout) ? PoseShout : Armed ? VisibleArmedIdlePose : PoseIdle);
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
            ClearFallingIn();
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
            _preferredSteerSide = 0;
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

        /// <summary>Emptying a gun into a machine. It is TickEngage with everything a
        /// MAN needs taken out: a car does not duck, does not shoot back and does not
        /// walk away, so there is no cover to look for, no flank to re-check and no
        /// chase. He walks into his own range, squares up, and fires until it is a
        /// wreck or the order is taken off him.</summary>
        void TickShootUp(float dt)
        {
            var car = CarMark;
            if (car == null || car.Tf == null || car.Wrecked || !Armed)
            {
                CarMark = null;
                State = Mode.Standing;
                Loco(dt, false);
                return;
            }

            var to = car.Tf.position - Tf.position;
            to.y = 0f;
            float dist = to.magnitude;
            float range = Ballistics.Range;

            // A MACHINE THAT IS DRIVING IS NOT WALKED AFTER. The same rule the fight
            // keeps for a motorcycle, and for the same reason: five men walking down a
            // street behind a car that is pulling away is a thing nobody does, and the
            // car wins the footrace anyway. He fires while it is in his reach and lets
            // it go when it is not - a car stopped, or crawling in traffic, he closes on.
            bool rolling = Mathf.Abs(car.Speed) > DrivingOff;
            // the same hysteresis the fight uses, so a man does not jog in place at the
            // line when the car he is shooting at rolls a metre
            bool closing = !rolling &&
                           (_wasClosing ? dist > range * RangeFactor : dist > range * 1.15f);
            _wasClosing = closing;
            if (closing)
            {
                TickStride(dt, car.Tf.position, range * RangeFactor, hurry: true,
                    run: RunWhile(dist > range * (_runningLeg ? RunOffFight : RunToFight)),
                    keepOffRoad: !OnCarriageway(car.Tf.position));
                return;
            }

            if (dist > 1e-3f) TurnCombat(to, 360f, dt);

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
                SetPose(HasPose(_firePose) ? _firePose : VisibleAimPose);
            }
            else
                SetPose(VisibleAimPose);
            TickBlend(dt);

            // squared up, and the arm actually up: the same two gates a man gets, for
            // the same reason - a round let off while the gun is still coming up goes
            // into the pavement
            if (_fireTimer <= 0f && dist <= range && CombatAimError(to) < 25f &&
                StrideAllowsAim(to) && _aimBlend >= 0.5f && BarrelOn(CarAim(car)))
            {
                _fireTimer = Ballistics.Interval;
                StartFirePose();
                Fired?.Invoke(this);
            }
        }

        void TickEngage(float dt)
        {
            // A man he was shooting at whose BODY has since been taken off the street
            // (the gore cleans up, a rival that broke and ran is despawned) is not null
            // the way a C# reference is null - his Tf is a destroyed Unity object, and
            // reading it throws. It threw every frame for every engaged man, which is
            // to say the fight stopped dead and nobody fired another round.
            // a man put on a machine instead of on a man: no cover, no closing on a
            // thing that walks, no waiting for it to stand up
            if (Target == null && CarMark != null) { TickShootUp(dt); return; }

            if (Target == null || !Target.Tf || Target.Dead || !Carrying)
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

            // TickArms normally draws before this branch. Keep the combat state safe
            // when Engage is called between ticks, and still fail honestly if this
            // body's rig cannot put the carried piece in its hand.
            if (!Armed && FightCloseEnoughToDraw)
            {
                DrawGun();
                if (!Armed)
                {
                    Target = null;
                    State = Mode.Standing;
                    Loco(dt, false);
                    return;
                }
            }

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
                var coverShot = Target.Tf.position - spot;
                coverShot.y = 0f;
                // The cover contract is about the shot FROM the flank, not where he
                // happens to be while walking to it. A stale/legacy flank beyond the
                // gun's real reach must be dropped; an in-range flank may be approached
                // from further away and the ordinary draw threshold still decides when
                // the rifle comes out.
                if (coverShot.magnitude > range)
                {
                    _coverSpot = null;
                    InCover = false;
                }
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
                    bool coverRouteFailed = TickCombatStride(dt, spot, 0.4f, hurry: true,
                        run: RunWhile(!_keepingLow && gap.magnitude > RunToCover));
                    // no way through to it (the car has rolled on, something else
                    // stands in the way): he fights from where he is instead
                    if (coverRouteFailed) { _coverSpot = null; _blockedFor = 0f; }
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
                            if (dist > 1e-3f) TurnCombat(toTarget, 240f, dt);
                            SetPose(VisibleCrouchPose);
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
                           (_wasClosing ? dist > range * RangeFactor : dist > range);
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
                //
                // Take this frame's step BEFORE judging the trigger. Obstacle and crowd
                // steering choose the real direction inside TickStride; judging first
                // would let one last round leave on the frame the step turns sideways.
                TickCombatStride(dt, Target.Tf.position, range * RangeFactor, hurry: true,
                    run: RunWhile(dist > range * (_runningLeg ? RunOffFight : RunToFight)),
                    attackEnvelope: true);
                if (dist <= range && !_runningLeg && _fireTimer <= 0f && _flinch <= 0f &&
                    _aimBlend >= 0.5f &&
                    CombatAimError(toTarget) < 40f &&
                    StrideAllowsAim(toTarget) && BarrelOn(Target))
                {
                    _fireTimer = Ballistics.Interval * OnTheMove;
                    StartFirePose();
                    Fired?.Invoke(this);
                }
                // WELL out of his reach he runs it in; inside a stride or two of it he
                // walks the rest, because a man who sprints to the line and stops dead
                // reads as a puppet being placed. The two figures are apart on purpose,
                // so a mark backing off a metre does not switch him between the two
                // every second.
                return;
            }

            if (dist > 1e-3f) TurnCombat(toTarget, 360f, dt);

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
                SetPose(HasPose(_firePose) ? _firePose : VisibleAimPose);
            }
            else
                SetPose(VisibleAimPose);
            TickBlend(dt);

            // shoot only once squared up - a man firing over his shoulder reads wrong -
            // and once the gun is actually raised on him (the aim blend): rising from a
            // duck, or fresh out of a flinch, the barrel spends a beat coming up, and a
            // round let off during it goes into the ground the clip was authored at
            float off = CombatAimError(toTarget);
            if (_fireTimer <= 0f && dist <= range && off < 25f && StrideAllowsAim(toTarget) &&
                _aimBlend >= 0.5f && BarrelOn(Target))
            {
                _fireTimer = Ballistics.Interval;
                StartFirePose();
                Fired?.Invoke(this);
            }
        }

        /// <summary>Choose the take that belongs to this long gun. This table is only
        /// reachable through the CoverDemo wardrobe; the city's existing PoseShoot is
        /// returned byte-for-byte as before.</summary>
        int FirePose()
        {
            if (UsesAuthoredLongGun)
            {
                // The authored rifle is automatic in this wardrobe and keeps the
                // same fast take even from cover.
                if (WeaponKind == EquipmentKind.Rifle && HasPose(PoseAutomaticShoot))
                    return PoseAutomaticShoot;
                if (InCover && HasPose(PoseCoverShoot)) return PoseCoverShoot;
                if (WeaponKind == EquipmentKind.TommyGun && HasPose(PoseAutomaticShoot))
                    return PoseAutomaticShoot;
                if (WeaponKind == EquipmentKind.Rifle && HasPose(PoseRifleGunplay))
                    return PoseRifleGunplay; // fallback if the automatic take is absent
                // The shotgun gets the separate, single-shot rifle take: unlike the
                // two gunplay loops it carries one complete recoil and recovery.
                if (WeaponKind == EquipmentKind.Shotgun && HasPose(PoseRifleShoot))
                    return PoseRifleShoot;
                if (HasPose(PoseRifleShoot)) return PoseRifleShoot;
            }
            return HasPose(PoseShoot) ? PoseShoot : -1;
        }

        void StartFirePose()
        {
            int pose = FirePose();
            if (pose < 0) return;
            bool rapid = UsesAuthoredLongGun && pose == PoseAutomaticShoot;
            bool startingRapidCycle = rapid && (_firePose != pose || _shootHold <= 0f);
            _firePose = pose;

            if (rapid)
            {
                // The take is 0.2 s long and the automatic ballistic beat is 0.14 s.
                // Speed the loop to one recoil per beat, keep it running between
                // rounds, and let each cadence tick make its own shot/flash/report.
                float rate = Mathf.Max(1f, PoseLength(pose) /
                    Mathf.Max(0.05f, Ballistics.Interval));
                if (startingRapidCycle) RestartPose(pose, 0f, rate);
                else SetPoseSpeed(pose, rate);
                _shootHold = Mathf.Max(_shootHold, Ballistics.Interval + 0.08f);
                return;
            }

            RestartPose(pose);
            _shootHold = UsesAuthoredLongGun
                ? PoseLength(pose)
                : Mathf.Min(PoseLength(pose), 0.45f);
            // An authored take finishes its recoil before another one starts. This is
            // demo-only through the wardrobe flag; ordinary city fire cadence is intact.
            if (UsesAuthoredLongGun)
            {
                _fireTimer = Mathf.Max(_fireTimer,
                    _shootHold + (pose == PoseCoverShoot ? 0.25f : 0f));
                if (pose == PoseCoverShoot)
                    _coverCycle = Mathf.Max(_coverCycle, _shootHold + 0.1f);
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
        bool BarrelOn(CrewWalker mark) => BarrelOn(mark.ChestPosition);

        /// <summary>Is the barrel actually on the thing he is shooting at? A man and a
        /// car ask the same question of the same muzzle, so they ask it here.</summary>
        bool BarrelOn(Vector3 at)
        {
            var to = at - MuzzlePosition;
            return to.magnitude < 2f || Vector3.Angle(MuzzleForward, to) < BarrelOnLimit;
        }

        /// <summary>How much slower a man shoots while he is still closing.</summary>
        const float OnTheMove = 1.5f;

        bool _wasClosing, _coverLooked;

        /// <summary>The fall has been run; do not run it again. Not the same question as
        /// <see cref="GunDropped"/> - a man who died with the gun still under his coat
        /// has had his fall run and dropped nothing.</summary>
        bool _gunFallDone;

        /// <summary>His gun has left his hand for the road, on the way down. A man who
        /// died with it still under his coat never dropped anything, and this stays
        /// false for him - which is the difference between a piece lying in the street
        /// and a piece the books still have against his name.</summary>
        public bool GunDropped { get; private set; }

        /// <summary>How far inside his gun's range this man closes to before he stops
        /// and fires - dealt per man, so a crew fans out into a loose line instead of
        /// piling onto one point.</summary>
        public float RangeFactor = 0.8f;

        // The gun slips out of the fist and settles flat on the ground beside him,
        // barrel where the hand was pointing. It stays a child of nothing: a prop now.
        void DropGun()
        {
            _gunFallDone = true;
            if (Weapon == null) return;   // it never came out; it goes down with him
            GunDropped = true;
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
            LocomotionPose = _keepingLow && HasPose(PoseCrouchWalk)
                ? VisibleCrouchWalkPose : VisibleWalkPose;
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

        /// <summary>Stood with nothing to do, free to be drawn into a word. A man POSTED
        /// on a door is not: two hoods covering a shopfront who turn to face each other
        /// for a chat are two hoods with their backs to the street, which is the one
        /// thing the post was for.</summary>
        public bool Loitering => State == Mode.Standing && _chatPartner == null && ChatCooldown <= 0f && !Alert && _shoutLeft <= 0f && !Retreating && !Watching;

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
                // A MAN POSTED ON A DOOR KEEPS HIS EYES WHERE HE WAS PUT. He still looks
                // about - a doorman who never moves his head is a statue - but either
                // side of the street, not right round to the shopfront his lieutenant
                // went into. Without this the glance is measured off whatever heading
                // his last stride happened to end on and the crew drifts round to face
                // the wall within a few seconds.
                if (Watching)
                    _lookYaw = _watchYaw + Random.Range(-32f, 32f);
                else if (Random.value < 0.7f)
                    _lookYaw = Tf.eulerAngles.y + Random.Range(-110f, 110f);
            }
            SpendLook(45f, dt);
            TickIdleLife(dt);
            Loco(dt, false);
        }

        /// <summary>
        /// Watch THIS way while he stands here. The one heading a standing man is given
        /// from outside: a crew posted at a shop door faces the street while its
        /// lieutenant is inside, and the idle glances play either side of it instead of
        /// wandering off it.
        ///
        /// Set AFTER the order that walks him to his place - every fresh order drops the
        /// watch, because a man sent somewhere else is not on that door any more.
        /// </summary>
        public void WatchToward(Vector3 way)
        {
            way.y = 0f;
            if (way.sqrMagnitude < 1e-4f)
                return;
            _watchYaw = Quaternion.LookRotation(way.normalized, Vector3.up).eulerAngles.y;
            _watching = true;
            _watchUntil = Time.time + WatchLease;
            // he turns onto it as soon as he is standing; the walk owns his heading
            // until then
            _lookYaw = _watchYaw;
        }

        /// <summary>Off the door: his head is his own again.</summary>
        public void StopWatching()
        {
            _watching = false;
            _watchYaw = 0f;
        }

        /// <summary>How long a posted man holds his door before he is one of the crew
        /// again. A post outlives the visit by design - men who covered a door go on
        /// covering it until they are told otherwise - but it must not outlive the game:
        /// while he is posted his crew's tether leaves him alone, and a man left posted
        /// for ever by an order nobody finished would never rejoin them.</summary>
        public const float WatchLease = 120f;

        /// <summary>Whether he is holding a heading somebody posted him on.</summary>
        public bool Watching => _watching && Time.time <= _watchUntil;

        bool _watching;
        float _watchYaw, _watchUntil;

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
        /// stretch to a door. A direct cross-city order deliberately has no old link;
        /// this attaches it again before a later pavement order. His feet stay where
        /// they are, the sideways gap kept as his lateral so the first step of the next
        /// order is a step, not a jump.</summary>
        public void Reseat(PedLink link, float t)
        {
            if (link == null || link.Length <= 0.01f || Dead || Riding) return;
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

        /// <summary>Over his head in the F3 tag: his name and his family, since two
        /// men of different mobs standing on one corner is the case the tag exists
        /// for.</summary>
        public override string DebugName =>
            (string.IsNullOrEmpty(DisplayName) ? "Hood" : DisplayName) +
            (Faction == 0 ? "  ·  the outfit" : "  ·  f" + Faction);

        public override string DebugState =>
            State + (Riding ? " · riding" : "") + (InCover ? " · in cover" : "") +
            (Ducked ? " · down" : "");

        /// <summary>What he means to do about it: the man he has his gun on and how far
        /// off he is, or the ground still to cover, and whatever is gearing his feet.
        /// The distance is the point - a tag that only says "Engaging" cannot tell a
        /// man closing on a fight from one stood in it.</summary>
        public override string DebugIntent
        {
            get
            {
                string line = StatusLine;
                if (Target != null && Target.Tf != null)
                    line += "  ·  " + Vector3.Distance(Tf.position, Target.Tf.position).ToString("F0") + " m off";
                else if (HasOrder && Tf != null)
                    line += "  ·  " + Vector3.Distance(Tf.position, Destination).ToString("F0") + " m to go";
                if (Retreating) line += "  ·  off the map";
                else if (Panicked) line += "  ·  nerve gone";
                if (Urgent) line += "  ·  ordered at the run";
                else if (Hustle) line += "  ·  quick feet";
                return line;
            }
        }

        // the words and what they were cut for: the debug tag over a man reads this
        // every frame, and a man on the move or in a fight concatenates a heading or a
        // name (the crew's chip on the top bar takes the short word, CrewStatus)
        string _statusLine;
        (Mode state, bool retreating, bool alert, CrewWalker target, bool ducked, bool inCover,
            CrewCar carMark, string heading) _statusKey;

        public string StatusLine
        {
            get
            {
                string heading = State == Mode.Walking || State == Mode.Striding ? PatrolInfo.Heading(Tf) : null;
                var key = (State, Retreating, Alert, Target, Ducked, InCover, CarMark, heading);
                if (_statusLine != null && key.Equals(_statusKey)) return _statusLine;
                _statusKey = key;
                _statusLine = State switch
                {
                    Mode.Standing => Retreating ? "Gone" : Alert ? "On alert - shots heard" : "Standing by",
                    Mode.Walking => "On the move, heading " + heading,
                    Mode.Striding => "On the move, heading " + heading,
                    Mode.Homing => "Almost there",
                    Mode.Engaging => Target != null
                        ? Ducked ? "Down behind cover - " + Target.DisplayName + " out there"
                          : InCover ? "Shooting from cover at " + Target.DisplayName
                          : "Shooting at " + Target.DisplayName
                        : CarMark != null ? "Shooting up the " + CarMark.DisplayName
                        : "Engaging",
                    Mode.Fleeing => Retreating ? "Getting out of here" : "Running for it",
                    Mode.Riding => "In the car",
                    Mode.Dead => "Down",
                    _ => string.Empty,
                };
                return _statusLine;
            }
        }
    }
}
