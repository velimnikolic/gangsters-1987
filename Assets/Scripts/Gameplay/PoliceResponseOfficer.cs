using System.Collections;
using UnityEngine;
using LivingCity.City;
using LivingCity.Entities;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// One responding officer - the pursuit half of the police, distinct from the ambient
    /// beat patrol (PoliceOfficerAgent), which stays on its rounds regardless. Spawned and
    /// pooled by PoliceResponseDirector, always hand-steered (his HumanBehavior is disabled
    /// at construction and never wakes), and driven by one lifecycle coroutine:
    ///
    ///   Respond(lastKnown) -> Search -> [sees him] -> Chase -> Engage -> Arrest | Shootout
    ///        ^                                          |
    ///        +---------------- lost him ----------------+
    ///
    /// The engage window is the game's central bargain: warningSeconds of "hands up!"
    /// during which surrendering ends it peacefully, opening fire or running ends it the
    /// other way, and breaking line of sight resets the hunt.
    ///
    /// Deliberately fallible everywhere the config says so: accuracy falls with distance
    /// and against a moving target, the chase is barely faster than the player's walk, and
    /// sight is a real raycast - a corner is a genuine escape.
    ///
    /// Carries InteractableNpc (added at construction), so the player can right-click-Kill
    /// an officer; that murder is reported instantly as AssaultingOfficer by WitnessSystem.
    /// </summary>
    public sealed class PoliceResponseOfficer : MonoBehaviour, UI.IOverlayStyledSubject
    {
        public enum State
        {
            Dormant, Responding, Searching, Chasing, Engaging, Shootout, Arresting, StandingDown,
        }

        const float NodeTolerance = 1.2f;
        const float NodeTimeout = 8f;

        /// <summary>Matches HumanBehavior's own animator scaling (speed * 0.8).</summary>
        const float AnimatorSpeedScale = 0.8f;

        /// <summary>Chest height on the player - where the sight ray and the shots go.</summary>
        const float ChestHeight = 1.3f;

        static readonly int SightBlockers = ~(1 << PedestrianSpawner.PedestrianLayer);
        static readonly WaitForFixedUpdate FixedStep = new WaitForFixedUpdate();
        static readonly WaitForSeconds ShortPoll = new WaitForSeconds(0.25f);

        PoliceResponseDirector director;
        PlayerMafioso player;
        PathFinding pathFinding;
        Animator animator;
        GunmanAim gunman;
        WeaponController weapon;
        PedestrianDeath death;
        PedestrianBody body;
        bool hasSpeedParam;
        bool hasActivityParam;

        State state;
        Coroutine life;

        // The throttled sight cache - one raycast per losTickSeconds, not per caller.
        float nextLosAt;
        bool losResult;
        float lastSeenAt = float.NegativeInfinity;
        Vector3 lastSeenPosition;

        public State CurrentState => state;

        public bool IsDown => death && death.IsDead;

        /// <summary>1-based, for the popup.</summary>
        public int UnitNumber { get; set; }

        PoliceConfig Police => GameplayRuntime.Police;

        int Wanted => WantedSystem.Instance ? WantedSystem.Instance.WantedLevel : 0;

        public void Configure(PoliceResponseDirector owner, PlayerMafioso target)
        {
            director = owner;
            player = target;

            pathFinding = GetComponent<PathFinding>();
            animator = GetComponent<Animator>();
            gunman = GetComponent<GunmanAim>();
            weapon = GetComponent<WeaponController>();
            death = GetComponent<PedestrianDeath>();

            hasSpeedParam = HasParameter(PedestrianAnimation.SpeedHash);
            hasActivityParam = HasParameter(PedestrianAnimation.ActivityHash);

            if (death)
                death.died += OnDown;
        }

        void OnEnable()
        {
            if (body == null)
                body = PedestrianRegistry.Register(transform);
            UI.OverlayRegistry.Register(this);
        }

        void OnDisable()
        {
            UI.OverlayRegistry.Unregister(this);
            PedestrianRegistry.Unregister(body);
            body = null;
        }

        void OnDestroy()
        {
            if (death)
                death.died -= OnDown;
        }

        /// <summary>Sent onto the street. The director has already positioned him.</summary>
        public void Deploy()
        {
            if (life != null)
                StopCoroutine(life);
            life = StartCoroutine(Life());
        }

        void OnDown()
        {
            // Shot dead mid-response. PedestrianDeath owns the shutdown; the registry body
            // is ours to hide, because our HumanBehavior never registered one of its own.
            if (life != null)
                StopCoroutine(life);
            life = null;
            state = State.Dormant;
            if (body != null)
            {
                body.SpeedMs = 0f;
                body.Hidden = true;
            }
            UI.OverlayRegistry.Unregister(this);
            if (director)
                director.OnOfficerDown(this);
        }

        // ------------------------------------------------------------------ lifecycle

        IEnumerator Life()
        {
            while (Wanted > 0 && !IsDown && player)
            {
                if (Seen())
                {
                    yield return Chase();
                    continue;
                }

                // No eyes on him - work the last known position.
                state = State.Responding;
                var target = WantedSystem.Instance ? WantedSystem.Instance.LastKnownPosition
                                                   : transform.position;
                var reportedAt = WantedSystem.Instance ? WantedSystem.Instance.LastKnownAt : 0f;

                yield return TravelTo(target, 3f, RespondSpeed, Seen);
                if (Seen() || Wanted == 0)
                    continue;

                yield return Search(target);
                if (Seen() || Wanted == 0)
                    continue;

                // Searched and found nothing. Hold the area until a fresh report moves the
                // last known position, the trail warms up, or the heat decays to nothing.
                state = State.Searching;
                while (Wanted > 0 && !Seen() &&
                       (!WantedSystem.Instance || WantedSystem.Instance.LastKnownAt <= reportedAt))
                    yield return ShortPoll;
            }

            yield return StandDown();
        }

        IEnumerator Search(Vector3 around)
        {
            state = State.Searching;
            var config = Police;
            var radius = config ? config.searchRadius : 12f;
            var points = config ? config.searchPoints : 3;
            var lookSeconds = config ? config.searchPointSeconds : 3f;

            for (var i = 0; i < points && !Seen() && Wanted > 0; i++)
            {
                var angle = Random.value * Mathf.PI * 2f;
                var point = around + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) *
                            (radius * (0.4f + 0.6f * Random.value));

                yield return GoTo(point, 1f, RespondSpeed, Seen);

                // Look around - a slow turn sells the search and sweeps the view cone.
                var until = Time.time + lookSeconds;
                while (Time.time < until && !Seen() && Wanted > 0)
                {
                    transform.Rotate(0f, 90f * Time.deltaTime, 0f);
                    yield return null;
                }
            }
        }

        /// <summary>
        /// The pursuit is ONE continuous steering loop reading the player's LIVE position
        /// every fixed step. The first version chased in 0.4-second WalkTo chunks, and
        /// every chunk began at speed zero - the officer stuttered along at under a
        /// walking pace and the walk cycle restarted four times a second, which read
        /// exactly as broken. Speed now ramps once and stays.
        /// </summary>
        IEnumerator Chase()
        {
            state = State.Chasing;
            var config = Police;
            var engageRange = config ? config.engageRange : 10f;
            var loseAfter = config ? config.loseSightSeconds : 4f;
            var speed = 0f;

            while (!IsDown && Wanted > 0 && player && !player.IsDead)
            {
                var seen = Seen();
                if (!seen && Time.time - lastSeenAt > loseAfter)
                    break; // Back to Life: respond to last known, search.

                var target = seen ? player.transform.position : lastSeenPosition;
                var toTarget = target - transform.position;
                var remaining = PedestrianSteering.Flat(toTarget).magnitude;

                if (seen && remaining <= engageRange &&
                    (!weapon || weapon.HasLineOfSightTo(
                        player.transform.position + Vector3.up * ChestHeight)))
                {
                    HaltBody();
                    yield return Engage();
                    if (state == State.StandingDown || state == State.Arresting)
                        yield break;
                    state = State.Chasing;
                    speed = 0f;
                    continue;
                }

                if (!seen && remaining <= 1.5f)
                {
                    // Standing where he was last seen with nothing in sight - hold still
                    // and let the lose-sight timer decide.
                    HaltBody();
                    yield return FixedStep;
                    continue;
                }

                speed = Mathf.MoveTowards(speed, ChaseSpeed, 4f * Time.deltaTime);

                var obstacle = PedestrianRegistry.Probe(body, toTarget);
                var heading = WallSlide(PedestrianSteering.Blend(toTarget, obstacle.Push));
                var advance = Mathf.Min(speed * Time.deltaTime,
                              Mathf.Min(obstacle.AllowedAdvance, remaining));

                var step = heading * advance;
                step.y = 0f;
                transform.position += step;

                if (heading != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(heading, Vector3.up);

                var actual = advance / Mathf.Max(Time.deltaTime, 1e-5f);
                body.SpeedMs = actual;
                SetSpeed(actual);

                yield return FixedStep;
            }

            HaltBody();
        }

        /// <summary>
        /// The warning window. "Hands up!" - then the player's choice decides which way
        /// this goes.
        /// </summary>
        IEnumerator Engage()
        {
            state = State.Engaging;
            var config = Police;
            var warning = config ? config.warningSeconds : 3f;
            var engageRange = config ? config.engageRange : 10f;

            HaltBody();
            if (gunman)
            {
                gunman.Ready();
                gunman.AimAt(player.transform);
            }

            var deadline = Time.time + warning;
            while (Time.time < deadline)
            {
                if (IsDown || Wanted == 0)
                    yield break;

                if (player.IsSurrendering)
                {
                    yield return Arrest();
                    yield break;
                }

                // He answered with the revolver - the window slams shut.
                if (player.CurrentState == PlayerMafioso.State.Shooting)
                    break;

                // Slipped out of sight mid-warning - back to the hunt.
                if (!Seen())
                {
                    LowerGun();
                    yield break;
                }

                // Running for it - warnings are for men who stand still.
                var flat = PedestrianSteering.Flat(player.transform.position - transform.position);
                if (flat.magnitude > engageRange * 1.3f)
                    break;

                yield return null;
            }

            yield return Shootout();
        }

        IEnumerator Shootout()
        {
            state = State.Shootout;
            var config = Police;
            var engageRange = config ? config.engageRange : 10f;
            var loseAfter = config ? config.loseSightSeconds : 4f;
            var fireWait = new WaitForSeconds(config ? config.fireInterval : 1.1f);

            while (!IsDown && Wanted > 0 && player && !player.IsDead)
            {
                if (player.IsSurrendering)
                {
                    yield return Arrest();
                    yield break;
                }

                if (!Seen())
                {
                    if (Time.time - lastSeenAt > loseAfter)
                    {
                        LowerGun();
                        yield break;
                    }
                    yield return ShortPoll;
                    continue;
                }

                var distance = PedestrianSteering
                    .Flat(player.transform.position - transform.position).magnitude;
                if (distance > engageRange * 1.6f)
                {
                    // Out of practical range - run him down rather than waste powder.
                    LowerGun();
                    yield break;
                }

                if (gunman)
                    gunman.Fire();
                if (weapon)
                    weapon.ResolveShotAtPlayer(player, Accuracy(distance),
                        config ? config.damagePerShot : 16f);

                yield return fireWait;
            }

            if (player && player.IsDead && director)
                director.OnPlayerDown();
        }

        IEnumerator Arrest()
        {
            state = State.Arresting;
            LowerGun();

            var config = Police;
            var arrestRange = config ? config.arrestRange : 1.8f;

            yield return GoTo(player.transform.position, arrestRange, RespondSpeed, null);

            if (!player.IsSurrendering)
                yield break; // Changed his mind and ran - Life resumes the chase.

            if (director)
                director.OnArrest();

            // Stand over him until the director clears the slate.
            while (player.IsSurrendering && Wanted > 0)
                yield return ShortPoll;
        }

        IEnumerator StandDown()
        {
            state = State.StandingDown;
            LowerGun();
            SetActivity(PedestrianAnimation.None);

            var config = Police;
            var linger = config ? config.standDownSeconds : 8f;

            // Amble off the scene, then hand back to the pool.
            var away = transform.position + transform.forward * 12f;
            yield return GoTo(away, 1.5f, RespondSpeed * 0.8f, null);
            yield return new WaitForSeconds(linger * 0.5f);

            if (director)
                director.Release(this);
        }

        // ------------------------------------------------------------------- senses

        /// <summary>
        /// Eyes on the player, throttled to one raycast per losTickSeconds however often
        /// the FSM asks. A hit refreshes the wanted system's contact - the officers ARE
        /// the city's eyes, so seeing and reporting are the same act.
        /// </summary>
        bool Seen()
        {
            if (Time.time < nextLosAt)
                return losResult;

            var config = Police;
            var tick = config ? config.losTickSeconds : 0.25f;
            nextLosAt = Time.time + tick;

            losResult = ComputeSight();
            if (losResult)
            {
                lastSeenAt = Time.time;
                lastSeenPosition = player.transform.position;
                if (WantedSystem.Instance)
                    WantedSystem.Instance.NotifySeen(lastSeenPosition, tick);
            }

            return losResult;
        }

        bool ComputeSight()
        {
            if (!player || player.IsDead)
                return false;

            var wanted = GameplayRuntime.Wanted;
            var range = wanted ? wanted.sightRadius : 25f;

            var from = transform.position + Vector3.up * 1.5f;
            var to = player.transform.position + Vector3.up * ChestHeight;
            var direction = to - from;
            if (PedestrianSteering.Flat(direction).sqrMagnitude > range * range)
                return false;

            var distance = direction.magnitude;
            if (distance < 0.5f)
                return true;

            return !Physics.Raycast(from, direction / distance, distance - 0.2f,
                                    SightBlockers, QueryTriggerInteraction.Ignore);
        }

        float Accuracy(float distance)
        {
            var config = Police;
            var accuracy = (config ? config.baseAccuracy : 0.9f)
                         - (config ? config.accuracyLossPerMeter : 0.05f) * distance;
            if (player && player.CurrentSpeedMs > 0.5f)
                accuracy -= config ? config.movingTargetPenalty : 0.25f;

            return Mathf.Clamp01(accuracy);
        }

        // ------------------------------------------------------------------ movement
        // Copied from PedestrianAgent via PlayerMafioso (see PoliceOfficerAgent's class
        // comment for the copy-not-share convention), parameterised by speed.

        float ChaseSpeed { get { var c = Police; return c ? c.chaseSpeed : 3.25f; } }

        float RespondSpeed { get { var c = Police; return c ? c.respondSpeed : 2.8f; } }

        IEnumerator TravelTo(Vector3 destination, float tolerance, float speedCap,
                             System.Func<bool> abortWhen)
        {
            destination.y = transform.position.y;
            waypoints.Clear();

            var flat = PedestrianSteering.Flat(destination - transform.position).magnitude;
            if (flat > 15f && pathFinding)
            {
                var route = pathFinding.GetPath(transform.position, destination, PathType.Sidewalk);
                if (route != null)
                {
                    for (var p = 0; p < route.Count; p++)
                    {
                        var path = route[p];
                        if (!path || path.pathPositions == null)
                            continue;

                        var nodes = path.pathPositions;
                        for (var i = p == 0 ? NearestNodeIndex(nodes) : 1; i < nodes.Count; i++)
                            if (nodes[i])
                                waypoints.Add(nodes[i].position);
                    }
                }
            }

            if (waypoints.Count == 0 ||
                PedestrianSteering.Flat(destination - waypoints[waypoints.Count - 1])
                    .magnitude <= 20f)
                waypoints.Add(destination);

            yield return FollowRoute(waypoints, tolerance, speedCap, abortWhen);
        }

        /// <summary>Single-point travel - the search legs, the arrest walk, standing down.</summary>
        IEnumerator GoTo(Vector3 point, float tolerance, float speedCap,
                         System.Func<bool> abortWhen)
        {
            waypoints.Clear();
            waypoints.Add(point);
            yield return FollowRoute(waypoints, tolerance, speedCap, abortWhen);
        }

        /// <summary>
        /// One steering loop over the whole list - speed carries across waypoints (see
        /// PlayerMafioso.FollowRoute for the surge this replaces), walls slide rather than
        /// swallow.
        /// </summary>
        IEnumerator FollowRoute(System.Collections.Generic.List<Vector3> points,
                                float finalTolerance, float speedCap,
                                System.Func<bool> abortWhen)
        {
            if (points.Count == 0)
                yield break;

            var speed = 0f;
            var index = 0;
            var deadline = Time.time + 15f + points.Count * 4f;

            while (index < points.Count && Time.time < deadline)
            {
                if (abortWhen != null && abortWhen())
                    break;

                var last = index == points.Count - 1;
                var target = points[index];
                var toTarget = target - transform.position;
                var remaining = PedestrianSteering.Flat(toTarget).magnitude;
                if (remaining <= (last ? finalTolerance : NodeTolerance))
                {
                    index++;
                    continue;
                }

                speed = Mathf.MoveTowards(speed, speedCap, 4f * Time.deltaTime);

                var obstacle = PedestrianRegistry.Probe(body, toTarget);
                var heading = WallSlide(PedestrianSteering.Blend(toTarget, obstacle.Push));
                var advance = Mathf.Min(speed * Time.deltaTime,
                              Mathf.Min(obstacle.AllowedAdvance, remaining));

                var step = heading * advance;
                step.y = Mathf.Clamp(toTarget.y, -advance, advance);
                transform.position += step;

                if (heading != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(heading, Vector3.up);

                var actual = advance / Mathf.Max(Time.deltaTime, 1e-5f);
                body.SpeedMs = actual;
                SetSpeed(actual);

                yield return FixedStep;
            }

            body.SpeedMs = 0f;
            SetSpeed(0f);
        }

        readonly System.Collections.Generic.List<Vector3> waypoints =
            new System.Collections.Generic.List<Vector3>(64);

        /// <summary>A knee-height ray one stride ahead; a hit slides the heading along the
        /// wall - the avoidance registry knows people, not architecture.</summary>
        Vector3 WallSlide(Vector3 heading)
        {
            if (heading == Vector3.zero)
                return heading;

            if (!Physics.Raycast(transform.position + Vector3.up * 0.9f, heading,
                                 out var hit, 1.1f, SightBlockers, QueryTriggerInteraction.Ignore))
                return heading;

            var slide = Vector3.ProjectOnPlane(heading, hit.normal);
            slide.y = 0f;
            return slide.sqrMagnitude > 1e-4f ? slide.normalized : Vector3.zero;
        }

        int NearestNodeIndex(System.Collections.Generic.List<Transform> nodes)
        {
            var best = 0;
            var bestSqr = float.MaxValue;
            for (var i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i])
                    continue;

                var sqr = (nodes[i].position - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ plumbing

        void HaltBody()
        {
            if (body != null)
                body.SpeedMs = 0f;
            SetSpeed(0f);
        }

        void LowerGun()
        {
            if (gunman)
            {
                gunman.LowerWeapon();
                gunman.Disengage();
            }
            SetActivity(PedestrianAnimation.None);
        }

        void SetSpeed(float metresPerSecond)
        {
            if (hasSpeedParam && animator)
                animator.SetFloat(PedestrianAnimation.SpeedHash, metresPerSecond * AnimatorSpeedScale);
        }

        void SetActivity(int value)
        {
            if (hasActivityParam && animator)
                animator.SetInteger(PedestrianAnimation.ActivityHash, value);
        }

        bool HasParameter(int nameHash)
        {
            if (!animator || !animator.runtimeAnimatorController)
                return false;
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == nameHash)
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ the overlay

        Transform UI.IOverlaySubject.OverlayAnchor => transform;
        float UI.IOverlaySubject.OverlayHeight => 2.3f;
        bool UI.IOverlaySubject.OverlayHidden => IsDown || state == State.Dormant;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Diamond;

        Color UI.IOverlaySubject.OverlayColor => state switch
        {
            State.Responding => new Color(0.25f, 0.5f, 0.95f),
            State.Searching => new Color(0.35f, 0.75f, 0.95f),
            State.Chasing => new Color(0.95f, 0.6f, 0.15f),
            State.Engaging => new Color(0.95f, 0.3f, 0.2f),
            State.Shootout => new Color(0.9f, 0.1f, 0.1f),
            State.Arresting => new Color(0.3f, 0.8f, 0.4f),
            _ => new Color(0.5f, 0.55f, 0.6f),
        };

        string UI.IOverlaySubject.OverlayTitle => $"Response Officer {UnitNumber}";

        string UI.IOverlaySubject.OverlayLine => state switch
        {
            State.Responding => "Responding to a report",
            State.Searching => "Searching the area",
            State.Chasing => "In pursuit!",
            State.Engaging => "\"Hands up!\"",
            State.Shootout => "Firing",
            State.Arresting => "Making the arrest",
            State.StandingDown => "Standing down",
            _ => "Idle",
        };

        long UI.IOverlaySubject.OverlayKey => (long)state;

        UI.MarkerStyle UI.IOverlayStyledSubject.MarkerStyle => new UI.MarkerStyle
        {
            SizeScale = 1.1f,
            Pulse = state is State.Chasing or State.Engaging or State.Shootout,
            PulsePeriod = 0.6f,
            PulseAmplitude = 0.25f,
        };
    }
}
