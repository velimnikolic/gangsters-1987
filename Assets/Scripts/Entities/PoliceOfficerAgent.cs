using System.Collections;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;

namespace LivingCity.Entities
{
    /// <summary>
    /// The brain of one beat officer: walk the pavements like anybody else, then - unlike
    /// anybody else - go HOME, through the station's own door, and come back out for another
    /// beat. Officers are persistent and few (config.policeOfficerCount), spawned and bound
    /// by PoliceDirector, and deliberately NOT PedestrianAgents: an officer never sits on a
    /// bench, never shops, never wanders into a stranger's building, and never joins the
    /// chat pairing - PedestrianInteractionDirector only iterates PedestrianAgent.Agents, so
    /// staying out of that list is the whole opt-out.
    ///
    /// Locomotion is the pack's HumanBehavior, exactly as for civilians, plus the scripted
    /// destination patch: patrolling is randomDestination wandering, the trip home sets
    /// human.scriptedDestination and rides the ordinary re-path queue. The off-graph legs at
    /// the door reuse the WalkTo/Face loop from PedestrianAgent VERBATIM rather than by
    /// refactor - that class is the 10k-crowd hot path, and extracting a base class for a
    /// four-instance feature is risk without payoff. If a third consumer ever appears,
    /// extract then.
    ///
    /// Crowd invariants honoured: the body registers once for the object's lifetime; every
    /// move goes through Probe/Blend and the AllowedAdvance clamp; Hidden and Stationary are
    /// kept truthful; re-paths flow through PedestrianRepathQueue (via HumanBehavior); no
    /// per-instance Update - one lifecycle coroutine plus the routeCompleted subscription.
    /// </summary>
    [RequireComponent(typeof(HumanBehavior))]
    public sealed class PoliceOfficerAgent : MonoBehaviour, UI.IOverlaySubject
    {
        /// <summary>Public because the overlay HUD colours its indicator and words its popup
        /// off it. The transitions stay this component's alone.</summary>
        public enum State { Patrolling, Returning, AtStation }

        /// <summary>Metres above the officer's origin to float the marker - the head is
        /// ~1.9m up.</summary>
        const float MarkerHeight = 2.3f;

        /// <summary>
        /// How near the station's doorstep a completed sidewalk route must end before the
        /// off-graph walk to the door takes over. Same tile-length reasoning as the patrol
        /// car's DockRange: the A* destination resolves to the nearest tile, so a genuine
        /// arrival is within one.
        /// </summary>
        const float ArriveRange = 20f;

        /// <summary>Give-up timer on any single off-graph leg - the PedestrianAgent value.</summary>
        const float OffGraphTimeout = 20f;

        /// <summary>Failed returns tolerated before the officer shrugs and patrols on.</summary>
        const int ReturnRetries = 3;

        /// <summary>Matches HumanBehavior's own animator scaling (speed * 0.8).</summary>
        const float AnimatorSpeedScale = 0.8f;

        static readonly WaitForSeconds ReappearPoll = new WaitForSeconds(0.5f);
        static readonly WaitForFixedUpdate FixedStep = new WaitForFixedUpdate();

        CityConfig config;
        PoliceStation station;
        System.Random rng;

        HumanBehavior human;
        Animator animator;
        Renderer[] renderers;
        PedestrianBody body;
        bool hasSpeedParam;

        State state;
        int routesRemaining;
        int returnAttempts;
        bool walkArrived;

        /// <summary>False until HumanBehavior has been enabled once and run its Start - the
        /// officer who begins the session indoors has a follower that has never pathed, and
        /// enabling it paths by itself; ResetRoute on top would path twice.</summary>
        bool humanStarted;

        public State CurrentState => state;
        public int RoutesRemaining => routesRemaining;

        /// <summary>Invisible right now - inside the station. The overlay hides the
        /// indicator with the body, or a diamond floats over an empty doorstep.</summary>
        public bool Hidden => body != null && body.Hidden;

        /// <summary>1-based, set by the director - "Officer 3" on the popup.</summary>
        public int UnitNumber { get; private set; }

        public void Configure(
            CityConfig cityConfig, PoliceStation home, int seed, bool startInside, int unitNumber)
        {
            config = cityConfig;
            station = home;
            rng = new System.Random(seed);
            UnitNumber = unitNumber;
            UI.OverlayRegistry.Register(this);
            human.routeCompleted += OnRouteCompleted;

            if (startInside)
            {
                // Same frame as Instantiate, so HumanBehavior.Start has not run and will not
                // until the first exit re-enables it.
                human.enabled = false;
                SetHidden(true);
                SetStationary(true);
                state = State.AtStation;
                StartCoroutine(StationRoutine(partlySpent: true));
            }
            else
            {
                humanStarted = true;
                routesRemaining = DrawRoutes();
                state = State.Patrolling;
            }
        }

        void Awake()
        {
            human = GetComponent<HumanBehavior>();
            animator = GetComponent<Animator>();
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        void Start()
        {
            hasSpeedParam = HasParameter(animator, PedestrianAnimation.SpeedHash);
        }

        void OnEnable()
        {
            if (body == null)
            {
                body = PedestrianRegistry.Register(transform);
                human.body = body;
            }
        }

        // --------------------------------------------------------------- the overlay
        // Explicit implementation - the HUD's plumbing, not the officer's own API.

        Transform UI.IOverlaySubject.OverlayAnchor => transform;
        float UI.IOverlaySubject.OverlayHeight => MarkerHeight;
        bool UI.IOverlaySubject.OverlayHidden => Hidden;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Diamond;
        Color UI.IOverlaySubject.OverlayColor => UI.PoliceIntention.OfficerColor(state);
        string UI.IOverlaySubject.OverlayTitle => UI.PoliceIntention.OfficerTitle(UnitNumber);
        string UI.IOverlaySubject.OverlayLine =>
            UI.PoliceIntention.OfficerIntention(state, routesRemaining);
        long UI.IOverlaySubject.OverlayKey => ((long)state << 32) | (uint)routesRemaining;

        // ---------------------------------------------------------------------------------

        void OnDestroy()
        {
            UI.OverlayRegistry.Unregister(this);
            PedestrianRegistry.Unregister(body);
            body = null;
            if (human)
                human.routeCompleted -= OnRouteCompleted;
        }

        void OnRouteCompleted()
        {
            switch (state)
            {
                case State.Patrolling:
                    routesRemaining--;
                    if (routesRemaining > 0)
                        return;

                    state = State.Returning;
                    returnAttempts = 0;
                    human.hasScriptedDestination = true;
                    human.scriptedDestination = station.DoorWorld;
                    return;

                case State.Returning:
                    var flat = transform.position - station.StandWorld;
                    flat.y = 0f;
                    if (flat.sqrMagnitude <= ArriveRange * ArriveRange)
                    {
                        // Disabling inside the handler is the take-over the patch was built
                        // for - MoveToNextPoint sees enabled false and never enqueues.
                        human.enabled = false;
                        human.hasScriptedDestination = false;
                        state = State.AtStation;
                        StartCoroutine(EnterRoutine());
                        return;
                    }

                    if (++returnAttempts >= ReturnRetries)
                    {
                        // The station is unreachable from here - a severed sidewalk, a
                        // pathological map. Walk the beat instead of orbiting forever.
                        human.hasScriptedDestination = false;
                        routesRemaining = DrawRoutes();
                        state = State.Patrolling;
                    }
                    return;
            }
        }

        /// <summary>The BuildingRoutine choreography against the station's own anchors.</summary>
        IEnumerator EnterRoutine()
        {
            var departure = transform.position;

            yield return WalkTo(station.StandWorld, 0.35f);
            if (!walkArrived)
            {
                // Same contract as every off-graph trip: walk back to the departure point so
                // the follower's stale targetPoint stays reachable, then resume the beat.
                yield return WalkTo(departure, 0.5f);
                ResumePatrol();
                yield break;
            }

            yield return Face(station.DoorWorld);
            yield return WalkTo(station.DoorWorld, 0.3f, 4f);

            SetHidden(true);
            SetStationary(true);

            yield return StationRoutine(partlySpent: false);
        }

        /// <summary>Inside the station: wait, reappear at the door, walk out, resume.</summary>
        IEnumerator StationRoutine(bool partlySpent)
        {
            var stay = Range(config.policeOfficerStationStayRange);
            yield return new WaitForSeconds(partlySpent ? stay * (float)rng.NextDouble() : stay);

            // Do not materialise inside somebody standing on the step.
            for (var waited = 0f;
                 waited < 10f && !PedestrianRegistry.IsClear(body, transform.position);
                 waited += 0.5f)
                yield return ReappearPoll;

            SetHidden(false);
            SetStationary(false);

            yield return Face(transform.position + station.Facing);
            yield return WalkTo(station.StandWorld, 0.4f);

            ResumePatrol();
        }

        void ResumePatrol()
        {
            routesRemaining = DrawRoutes();
            state = State.Patrolling;
            human.hasScriptedDestination = false;
            human.enabled = true;

            if (humanStarted)
            {
                // The follower's route belongs to wherever the beat ended; throw it away and
                // path from the doorstep, through the budget queue like everybody else.
                human.ResetRoute();
            }
            else
            {
                // First enable runs HumanBehavior.Start, which paths by itself.
                humanStarted = true;
            }
        }

        // ------------------------------------------------------------------ movement
        // Copied from PedestrianAgent (see the class comment for why copied, not shared).

        IEnumerator WalkTo(Vector3 target, float stopWithin, float timeout = OffGraphTimeout)
        {
            SetStationary(false);
            var speed = 0f;
            var deadline = Time.time + timeout;

            while (Time.time < deadline)
            {
                var toTarget = target - transform.position;
                var remaining = PedestrianSteering.Flat(toTarget).magnitude;
                if (remaining <= stopWithin)
                    break;

                speed = Mathf.MoveTowards(speed, human.maxspeed, 4f * Time.deltaTime);

                var obstacle = PedestrianRegistry.Probe(body, toTarget);
                var heading = PedestrianSteering.Blend(toTarget, obstacle.Push);
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
            walkArrived =
                PedestrianSteering.Flat(target - transform.position).magnitude <= stopWithin * 2f + 0.5f;
        }

        IEnumerator Face(Vector3 point, float seconds = 0.35f)
        {
            var direction = PedestrianSteering.Flat(point - transform.position);
            if (direction.sqrMagnitude < 1e-4f)
                yield break;

            var from = transform.rotation;
            var to = Quaternion.LookRotation(direction.normalized, Vector3.up);
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                transform.rotation = Quaternion.Slerp(from, to, t / seconds);
                yield return null;
            }
            transform.rotation = to;
        }

        // ------------------------------------------------------------------ plumbing

        void SetStationary(bool stationary)
        {
            if (body == null)
                return;

            body.Stationary = stationary;
            if (stationary)
            {
                body.SpeedMs = 0f;
                SetSpeed(0f);
            }
        }

        void SetHidden(bool hidden)
        {
            if (body != null)
                body.Hidden = hidden;

            foreach (var r in renderers)
                if (r)
                    r.enabled = !hidden;
        }

        void SetSpeed(float metresPerSecond)
        {
            if (hasSpeedParam)
                animator.SetFloat(PedestrianAnimation.SpeedHash, metresPerSecond * AnimatorSpeedScale);
        }

        int DrawRoutes()
        {
            var routes = config.policeOfficerPatrolRoutes;
            return Mathf.Max(1, rng.Next(Mathf.Min(routes.x, routes.y),
                                         Mathf.Max(routes.x, routes.y) + 1));
        }

        float Range(Vector2 range) =>
            range.x + (float)rng.NextDouble() * Mathf.Max(0f, range.y - range.x);

        static bool HasParameter(Animator animator, int nameHash)
        {
            if (!animator || !animator.runtimeAnimatorController)
                return false;

            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == nameHash)
                    return true;

            return false;
        }
    }
}
