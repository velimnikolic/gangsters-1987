using System.Collections;
using UnityEngine;
using LivingCity.City;
using LivingCity.Entities;
using LivingCity.UI;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The player's man on the street: a man-mafia rig commanded by clicks. One order at a
    /// time - move here, kill him - each an interruptible coroutine; a new order throws the
    /// old one away and resets every hold the old one had on the pose.
    ///
    /// Deliberately carries NO HumanBehavior. Officers keep it disabled because they
    /// alternate between graph-commanded travel and hand-steering; the player is ALWAYS
    /// hand-steered from input, so the pack component would contribute nothing but its
    /// stale-state traps (targetPoint survives a disable). Routes still come from the same
    /// A* - a PathFinding component is a stateless service and rides along - and the walking
    /// itself is the Probe/Blend/clamp loop copied from PedestrianAgent VERBATIM, per the
    /// project's own convention (see PoliceOfficerAgent's class comment on why copied, not
    /// shared).
    ///
    /// Crowd invariants honoured like every other walker: the body registers once for the
    /// object's lifetime, every step goes through Probe/Blend and the AllowedAdvance clamp,
    /// SpeedMs is kept truthful, and there is no per-frame work outside the active order.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(PathFinding))]
    public sealed class PlayerMafioso : MonoBehaviour, IOverlayStyledSubject
    {
        /// <summary>Public because the HUD and the popup word themselves off it.</summary>
        public enum State { Idle, Moving, Aiming, Shooting, Surrendering, Dead }

        /// <summary>Give-up timer on a single node-to-node leg. Legs are a few metres.</summary>
        const float NodeTimeout = 8f;

        /// <summary>Give-up timer on the final off-graph leg - the PedestrianAgent value.</summary>
        const float OffGraphTimeout = 20f;

        /// <summary>Matches HumanBehavior's own animator scaling (speed * 0.8).</summary>
        const float AnimatorSpeedScale = 0.8f;

        /// <summary>Kill-order approaches tolerated before the player shrugs and stands down.</summary>
        const int ApproachRetries = 3;

        [Tooltip("Identity, movement and marker numbers. Falls back to GameplayRuntime, " +
                 "then to class defaults, when empty.")]
        [SerializeField] PlayerConfig config;

        Animator animator;
        PathFinding pathFinding;
        GunmanAim gunman;
        WeaponController weapon;
        PlayerArsenal arsenal;
        PedestrianBody body;
        bool hasSpeedParam;
        bool hasActivityParam;

        Coroutine order;
        State state;
        CapsuleCollider capsule;

        /// <summary>SetSpeed as a delegate, made once - the walk loop takes it per route.</summary>
        System.Action<float> speedSetter;

        /// <summary>Where an arrest or a death puts him back. His starting kerb, for now;
        /// a real hideout building can take over later without touching the respawn flow.</summary>
        Vector3 hideout;

        public State CurrentState => state;

        public float Health { get; private set; }
        public float MaxHealth { get; private set; }

        public bool IsDead => state == State.Dead;

        public bool IsSurrendering => state == State.Surrendering;

        /// <summary>How fast he is moving right now - the police accuracy model reads it.</summary>
        public float CurrentSpeedMs => body?.SpeedMs ?? 0f;

        /// <summary>Fired once on death - the WASTED flow hangs off it.</summary>
        public event System.Action Died;

        public int WantedLevel =>
            WantedSystem.Instance ? WantedSystem.Instance.WantedLevel : 0;

        PlayerConfig Config => config ? config : GameplayRuntime.Player;

        CombatConfig Combat => GameplayRuntime.Combat;

        float WalkSpeed { get { var c = Config; return c ? c.walkSpeed : 3f; } }

        /// <summary>Resolved lazily, NOT in Awake: GameplayBootstrap AddComponents the
        /// arsenal after this object already exists, so an Awake-time cache would be a
        /// permanent null.</summary>
        PlayerArsenal Arsenal => arsenal ? arsenal : arsenal = GetComponent<PlayerArsenal>();

        void Awake()
        {
            animator = GetComponent<Animator>();
            pathFinding = GetComponent<PathFinding>();
            gunman = GetComponent<GunmanAim>();
            weapon = GetComponent<WeaponController>();
            speedSetter = SetSpeed;
        }

        void Start()
        {
            hasSpeedParam = RouteWalker.HasParameter(animator, PedestrianAnimation.SpeedHash);
            hasActivityParam = RouteWalker.HasParameter(animator, PedestrianAnimation.ActivityHash);

            MaxHealth = Config ? Config.maxHealth : 100f;
            Health = MaxHealth;

            capsule = GetComponent<CapsuleCollider>();
            hideout = transform.position;
        }

        void OnEnable()
        {
            if (body == null)
                body = PedestrianRegistry.Register(transform);

            OverlayRegistry.Register(this);
        }

        // The overlay entry comes and goes with enabled state - the registry appends
        // without dedup, so an enable/disable cycle that only unregistered at destroy
        // would leave a second marker over the same man. The crowd body deliberately
        // stays: it registers once for the object's lifetime (class note above).
        void OnDisable()
        {
            OverlayRegistry.Unregister(this);
        }

        void OnDestroy()
        {
            PedestrianRegistry.Unregister(body);
            body = null;
        }

        // --------------------------------------------------------------------- orders

        /// <summary>Walk to a clicked point. The A* snaps it to the sidewalk graph.
        /// Moving out of a surrender is allowed - that is what fleeing IS.</summary>
        public void OrderMove(Vector3 point)
        {
            if (!IsDead)
                SetOrder(MoveOrder(point));
        }

        /// <summary>Close to engage range, aim, and fire until the target is down.</summary>
        public void OrderKill(InteractableNpc target)
        {
            if (!IsDead && target && !target.IsDead)
                SetOrder(KillOrder(target));
        }

        public void Stop()
        {
            if (!IsDead)
                SetOrder(null);
        }

        // ---------------------------------------------------------------- consequences

        /// <summary>Police fire lands here. Nothing else damages him yet.</summary>
        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            Health = Mathf.Max(0f, Health - amount);
            if (Health <= 0f)
                Die();
        }

        /// <summary>Hands up. No surrender take exists in any pack, so the pose is a stand-
        /// still + the state on the popup and HUD; the ARREST flow is unaffected.</summary>
        public void Surrender()
        {
            if (IsDead || state == State.Surrendering)
                return;

            SetOrder(null);
            state = State.Surrendering;
        }

        void Die()
        {
            SetOrder(null);
            state = State.Dead;

            SetActivity(PedestrianAnimation.Die);
            if (body != null)
            {
                body.SpeedMs = 0f;
                // Out of the crowd's probes - people walk around a body by geometry, not
                // by avoidance, exactly as PedestrianDeath does it.
                body.Hidden = true;
            }
            if (capsule)
                capsule.enabled = false;

            Died?.Invoke();
        }

        /// <summary>
        /// Back on his feet at the hideout. The Rebind+Update pair is PedestrianDeath.
        /// Revive's trick - without it the machine sits on the death clip's last frame and
        /// the body stays face-down for a frame after teleporting.
        /// </summary>
        public void Respawn()
        {
            SetOrder(null);

            transform.position = hideout;
            if (body != null)
            {
                // Teleports bypass the probes, and only probes re-bucket the spatial hash.
                PedestrianRegistry.Rebucket(body, hideout);
                body.Hidden = false;
                body.SpeedMs = 0f;
            }
            if (capsule)
                capsule.enabled = true;

            Health = MaxHealth;
            state = State.Idle;

            SetActivity(PedestrianAnimation.None);
            if (animator)
            {
                animator.Rebind();
                animator.Update(0f);
            }
        }

        /// <summary>
        /// One order at a time. Stopping a coroutine mid-walk leaves whatever it was doing
        /// half-done - a raised gun, a non-zero speed float (the feet-slide trap) - so the
        /// reset here is unconditional.
        /// </summary>
        void SetOrder(IEnumerator routine)
        {
            if (order != null)
                StopCoroutine(order);
            order = null;

            if (gunman)
                gunman.Disengage();
            SetActivity(PedestrianAnimation.None);
            if (body != null)
                body.SpeedMs = 0f;
            SetSpeed(0f);
            state = State.Idle;

            if (routine != null)
                order = StartCoroutine(Run(routine));
        }

        /// <summary>Wraps every order so natural completion always lands back in Idle.</summary>
        IEnumerator Run(IEnumerator routine)
        {
            yield return routine;
            order = null;
            state = State.Idle;
        }

        IEnumerator MoveOrder(Vector3 destination)
        {
            state = State.Moving;
            yield return TravelTo(destination, ArriveTolerance, null);
        }

        IEnumerator KillOrder(InteractableNpc target)
        {
            var combat = Combat;
            // Range and cadence come through the arsenal - a bought weapon changes the
            // engagement, not just the damage. Absent (the bootstrap never ran), the old
            // CombatConfig numbers apply untouched.
            var arms = Arsenal;
            var engageRange = arms ? arms.EngageRange(combat)
                                   : combat ? combat.engageRange : 4f;
            var aimWait = new WaitForSeconds(combat ? combat.aimSeconds : 1.2f);
            var fireWait = new WaitForSeconds(arms ? arms.FireInterval(combat)
                                                   : combat ? combat.fireInterval : 0.9f);

            bool InRange() =>
                target && !target.IsDead
                && PedestrianSteering.Flat(target.transform.position - transform.position)
                       .sqrMagnitude <= engageRange * engageRange
                && (!weapon || weapon.HasLineOfSight(target));

            var approaches = 0;
            while (target && !target.IsDead)
            {
                if (!InRange())
                {
                    if (++approaches > ApproachRetries)
                        break;

                    // Gun away for the walk: the pistol activities override the locomotion
                    // states, so a drawn gun would turn the run into a sliding pose.
                    if (gunman)
                        gunman.Disengage();
                    SetActivity(PedestrianAnimation.None);

                    state = State.Moving;
                    // The predicate rides inside the hot walk loop - the approach stops the
                    // instant the shot is on, not at the next waypoint.
                    yield return TravelTo(target.transform.position, engageRange * 0.7f, InRange);
                    if (!InRange())
                        continue;
                }

                state = State.Aiming;
                if (gunman)
                {
                    gunman.Ready();
                    gunman.AimAt(target.transform);
                }
                yield return aimWait;

                state = State.Shooting;
                while (InRange())
                {
                    if (gunman)
                        gunman.Fire();
                    if (weapon)
                        weapon.ResolveShot(target);

                    yield return fireWait;

                    if (!target || target.IsDead)
                        break;
                }

                if (!target || target.IsDead)
                    break;

                // Lost range or line of sight mid-volley - lower the gun and re-approach.
                if (gunman)
                    gunman.LowerWeapon();
            }

            // The beat after the shot: gun stays up over where they fell, then comes down.
            yield return new WaitForSeconds(combat ? combat.lowerWeaponAfter : 1.5f);
            if (gunman)
            {
                gunman.LowerWeapon();
                gunman.Disengage();
            }
            SetActivity(PedestrianAnimation.None);
        }

        // ------------------------------------------------------------------- movement

        float ArriveTolerance { get { var c = Config; return c ? c.arriveTolerance : 0.35f; } }

        float DirectWalkRange { get { var c = Config; return c ? c.directWalkRange : 15f; } }

        /// <summary>
        /// Route to the destination along the sidewalk graph, then close the last stretch
        /// off-graph, bank-customer style. The destination is flattened to the player's own
        /// height first: a click can land on a roof, and the y-clamp in the steering loop
        /// would otherwise climb toward it a few centimetres per step.
        /// </summary>
        IEnumerator TravelTo(Vector3 destination, float tolerance, System.Func<bool> abortWhen)
        {
            destination.y = transform.position.y;
            waypoints.Clear();

            var flat = PedestrianSteering.Flat(destination - transform.position).magnitude;
            if (flat > DirectWalkRange)
            {
                var route = pathFinding.GetPath(transform.position, destination, PathType.Sidewalk);
                if (route == null)
                {
                    // Off the tile network entirely - the map edge, a rebuilt city. The A*'s
                    // endpoint resolution already snaps anything plausible to the nearest
                    // walkable tile, so a null is genuinely unreachable, not merely awkward.
                    Debug.LogWarning("[PlayerMafioso] No route to the clicked point - staying put.", this);
                    yield break;
                }

                for (var p = 0; p < route.Count; p++)
                {
                    var path = route[p];
                    if (!path || path.pathPositions == null)
                        continue;

                    var nodes = path.pathPositions;

                    // The graph's paths are directed, so nodes always run in travel order.
                    // The first path is entered wherever the player already stands;
                    // consecutive paths share their boundary node, so later ones skip it -
                    // both exactly as HumanBehavior walks the same lists.
                    for (var i = p == 0 ? RouteWalker.NearestNodeIndex(nodes, transform.position) : 1;
                         i < nodes.Count; i++)
                        if (nodes[i])
                            waypoints.Add(nodes[i].position);
                }

                // The off-graph leg - onto a forecourt, to the middle of a plaza. Capped: a
                // destination the route could not get NEAR (deep inside a block) stays
                // unreached rather than walked to through a building.
                if (waypoints.Count == 0 ||
                    PedestrianSteering.Flat(destination - waypoints[waypoints.Count - 1])
                        .magnitude <= DirectWalkRange)
                    waypoints.Add(destination);
            }
            else
            {
                waypoints.Add(destination);
            }

            // The shared steering loop - see RouteWalker for why the speed carries across
            // nodes and why walls slide rather than swallow.
            yield return RouteWalker.FollowRoute(transform, body, waypoints, tolerance, WalkSpeed,
                                                 WallMask, abortWhen, speedSetter);
        }

        readonly System.Collections.Generic.List<Vector3> waypoints =
            new System.Collections.Generic.List<Vector3>(64);

        /// <summary>What blocks walking: everything except the crowd's own layer.</summary>
        static readonly int WallMask = ~(1 << PedestrianSpawner.PedestrianLayer);

        // ------------------------------------------------------------------- plumbing

        void SetSpeed(float metresPerSecond)
        {
            if (hasSpeedParam)
                animator.SetFloat(PedestrianAnimation.SpeedHash, metresPerSecond * AnimatorSpeedScale);
        }

        void SetActivity(int value)
        {
            if (hasActivityParam)
                animator.SetInteger(PedestrianAnimation.ActivityHash, value);
        }

        // ------------------------------------------------------------------ the overlay
        // Explicit implementation - the HUD's plumbing, not the player's own API.

        Transform IOverlaySubject.OverlayAnchor => transform;
        float IOverlaySubject.OverlayHeight { get { var c = Config; return c ? c.markerHeight : 2.3f; } }
        bool IOverlaySubject.OverlayHidden => false;
        OverlayShape IOverlaySubject.MarkerShape => OverlayShape.Diamond;
        Color IOverlaySubject.OverlayColor { get { var c = Config; return c ? c.markerColor : Color.red; } }
        string IOverlaySubject.OverlayTitle { get { var c = Config; return c && !string.IsNullOrEmpty(c.playerName) ? c.playerName : "The Boss"; } }
        string IOverlaySubject.OverlayLine =>
            $"Health {Health:0}/{MaxHealth:0}  ·  Wanted {WantedLevel}  ·  {Describe(state)}";
        long IOverlaySubject.OverlayKey =>
            ((long)state << 40) | ((long)WantedLevel << 32) | (uint)Mathf.RoundToInt(Health);

        MarkerStyle IOverlayStyledSubject.MarkerStyle
        {
            get
            {
                var c = Config;
                return new MarkerStyle
                {
                    SizeScale = c ? c.markerSizeScale : 1.25f,
                    Pulse = true,
                    PulsePeriod = c ? c.markerPulsePeriod : 1.2f,
                    PulseAmplitude = c ? c.markerPulseAmplitude : 0.22f,
                    AlwaysVisible = true,
                };
            }
        }

        static string Describe(State state) => state switch
        {
            State.Idle => "Waiting",
            State.Moving => "On the move",
            State.Aiming => "Taking aim",
            State.Shooting => "Shooting",
            State.Surrendering => "Hands up",
            State.Dead => "Dead",
            _ => "",
        };
    }
}
