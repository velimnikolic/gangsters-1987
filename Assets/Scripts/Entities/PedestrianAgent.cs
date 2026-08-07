using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;

namespace LivingCity.Entities
{
    /// <summary>
    /// The interaction brain riding on every spawned pedestrian: chats and arguments (on the
    /// director's command), bench sits, shop visits, going in and out of buildings, and plain
    /// idling (all rolled locally when an opportunity is nearby). Subsumes PedestrianIdler.
    ///
    /// The contract with HumanBehavior is the one PedestrianIdler proved out, extended: the
    /// pack script has no idle state and re-paths on arrival, so the only safe intervention
    /// is to disable it outright - and while it is disabled, THIS component owns the
    /// transform and the Animator's speed float. Off-graph trips (to a bench, to a door)
    /// record the departure point and return to it before re-enabling, so HumanBehavior
    /// resumes toward its old targetPoint from roughly where it left the pavement and its
    /// untouched state stays valid.
    ///
    /// Owns this walker's entry in <see cref="PedestrianRegistry"/> - registered for the
    /// lifetime of the OBJECT, not of HumanBehavior's enabled state, because a person
    /// standing in a conversation is exactly the obstacle the registry exists to report.
    /// Off-graph walking runs the same Probe/Blend/clamp loop as the patched follower, so a
    /// detour to a bench steers around people exactly like the pavement walk does.
    /// </summary>
    [RequireComponent(typeof(HumanBehavior))]
    public sealed class PedestrianAgent : MonoBehaviour
    {
        /// <summary>Live agents, maintained alongside registry membership. Read by the director.</summary>
        public static readonly List<PedestrianAgent> Agents = new List<PedestrianAgent>();

        /// <summary>
        /// Seconds between opportunity rolls while walking free. Public because the director
        /// paces its per-frame slice off it - see TickOpportunities.
        /// </summary>
        public const float RollInterval = 2f;

        /// <summary>How far off the pavement a walker will detour for a bench or a door.</summary>
        const float OpportunityRange = 9f;

        /// <summary>Give-up timer on any single off-graph leg. Generous - it is a backstop.</summary>
        const float OffGraphTimeout = 20f;

        /// <summary>
        /// Seconds the stand-up clip is given to play out before the walk resumes. Sitting-Idle
        /// is 33 keys at 24fps = 1.38s; the old 2.4 left a second of standing at attention.
        /// </summary>
        const float SitTransitionSeconds = 1.5f;

        /// <summary>Matches HumanBehavior's own animator scaling (speed * 0.8).</summary>
        const float AnimatorSpeedScale = 0.8f;

        // Fixed-duration yields, shared across all agents. The drawn-per-activity durations
        // (sit, shop, idle, chat) stay as fresh WaitForSeconds - they differ every time.
        static readonly WaitForSeconds ExitBeat = new WaitForSeconds(0.4f);
        static readonly WaitForSeconds ReappearPoll = new WaitForSeconds(0.5f);
        static readonly WaitForFixedUpdate FixedStep = new WaitForFixedUpdate();

        CityConfig config;
        System.Random rng;

        HumanBehavior human;
        Animator animator;
        Renderer[] renderers;
        PedestrianBody body;
        bool hasSpeedParam;
        bool hasActivityParam;

        Coroutine activity;
        float cooldownUntil;
        float nextRollAt;
        bool walkArrived;
        Vector3 departure;

        BenchSeats claimedBench;
        int claimedSeat = -1;

        BuildingDoor claimedDoor;
        BuildingDoor insideOf;

        // The road test matters here: IsMoving only rules out the red-light wait, and a
        // crossing is an ordinary Sidewalk path across the asphalt - without it a pair
        // meeting mid-crossing parks in front of the traffic for the whole conversation.
        public bool AvailableForChat =>
            activity == null && rng != null && Time.time >= cooldownUntil
            && human && human.enabled && human.IsMoving
            && !RoadSurface.IsOnRoad(transform.position);

        public void Configure(CityConfig cityConfig, int seed)
        {
            config = cityConfig;
            rng = new System.Random(seed);

            // Staggered grace period so a freshly spawned crowd does not all decide to
            // perform in the same second.
            cooldownUntil = Time.time + Range(new Vector2(5f, config.interactionCooldownRange.y * 0.5f));
        }

        void Awake()
        {
            human = GetComponent<HumanBehavior>();
            animator = GetComponent<Animator>();
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        void Start()
        {
            // Parameter presence is checked against the controller the spawner assigned, not
            // assumed: with the master toggle half-on (agent added, controller missing) the
            // agent must degrade to walking and idling rather than spam warnings.
            hasSpeedParam = HasParameter(animator, PedestrianAnimation.SpeedHash);
            hasActivityParam = HasParameter(animator, PedestrianAnimation.ActivityHash);
        }

        void OnEnable()
        {
            if (body == null)
            {
                body = PedestrianRegistry.Register(transform);
                human.body = body;
                Agents.Add(this);
            }
        }

        void OnDestroy()
        {
            PedestrianRegistry.Unregister(body);
            Agents.Remove(this);
            body = null;
        }

        void OnDisable()
        {
            // Never leave a pedestrian frozen, hidden or squatting on a seat claim if this
            // component goes away mid-activity.
            if (activity == null)
                return;

            StopCoroutine(activity);
            activity = null;
            Restore();
        }

        /// <summary>
        /// The opportunity roll, formerly this component's Update. At crowd scale ten
        /// thousand per-instance Update callbacks are pure interop overhead - almost all of
        /// them early-out on a timer - so the director walks the agent list in slices and
        /// calls this instead. The internal timers are unchanged: however often the director
        /// visits, an agent still rolls at most once per RollInterval.
        /// </summary>
        public void TickOpportunities()
        {
            if (activity != null || rng == null || body == null)
                return;
            if (Time.time < cooldownUntil || Time.time < nextRollAt)
                return;

            nextRollAt = Time.time + RollInterval;

            if (!human.enabled || !human.IsMoving)
                return;

            // Nobody decides to sit, shop or loiter while standing on the carriageway. This
            // also keeps every routine's departure point on the pavement, which is where the
            // walk-back-and-resume contract returns the agent to.
            if (RoadSurface.IsOnRoad(transform.position))
                return;

            var director = PedestrianInteractionDirector.Instance;

            // The roll comes BEFORE the search on purpose: a failed roll must not claim (and
            // then have to release) a seat somebody else could have taken this tick.
            if (director && rng.NextDouble() < config.benchChance
                && director.TryClaimSeat(transform.position, OpportunityRange, out var bench, out var seat))
            {
                claimedBench = bench;
                claimedSeat = seat;
                Begin(SitRoutine(bench, seat));
                return;
            }

            if (director && rng.NextDouble() < config.shopChance
                && director.TryPickShop(transform.position, OpportunityRange, out var shop))
            {
                Begin(ShopRoutine(shop));
                return;
            }

            // After the shopfront roll, so a cafe still wins on a street where the same
            // building carries both markers. Claiming the doorstep is part of the condition -
            // if somebody is already on it, this walker simply carries on past.
            if (director && rng.NextDouble() < config.buildingVisitChance
                && director.TryPickDoor(transform.position, OpportunityRange, out var door)
                && door.TryClaimStep())
            {
                claimedDoor = door;
                Begin(BuildingRoutine(door));
                return;
            }

            // Scaled down because this needs no nearby prop: unscaled, a 0.3 chance per roll
            // would have everybody stopping within seconds of their cooldown expiring.
            if (rng.NextDouble() < config.idleChance * 0.2f)
                Begin(IdleRoutine());
        }

        /// <summary>Director's command. Both members of the pair receive it on the same tick.</summary>
        public void BeginConversation(Vector3 partnerPos, float duration, bool argue)
        {
            if (activity != null || rng == null || RoadSurface.IsOnRoad(transform.position))
                return;

            Begin(ConversationRoutine(partnerPos, duration, argue));
        }

        void Begin(IEnumerator routine)
        {
            activity = StartCoroutine(routine);
        }

        // ------------------------------------------------------------------ activities

        IEnumerator ConversationRoutine(Vector3 partnerPos, float duration, bool argue)
        {
            Halt();

            // Close the gap a little first - pairs are found within earshot, not within
            // conversation distance. The avoidance clamp is the spacing: each stops where
            // the other's minSeparation says, so nobody overlaps however short the walk.
            // Both members were on a pavement when paired, but at a junction corner the
            // midpoint between them can be on the asphalt. Both compute the same midpoint
            // from same-tick positions, so either both close the gap or both stay put and
            // talk across the corner.
            var midpoint = (transform.position + partnerPos) * 0.5f;
            if (RoadSurface.IsOnRoad(midpoint))
                midpoint = transform.position;
            yield return WalkTo(midpoint, argue ? 0.5f : 0.7f, 4f);

            SetStationary(true);
            yield return Face(partnerPos);

            SetActivity(argue ? PedestrianAnimation.Argue : PedestrianAnimation.Talk);
            yield return new WaitForSeconds(duration);
            SetActivity(PedestrianAnimation.None);

            // A beat for the exit transition before the walk cycle takes over.
            yield return ExitBeat;

            Finish();
        }

        IEnumerator SitRoutine(BenchSeats bench, int seat)
        {
            Halt();
            departure = transform.position;

            yield return WalkTo(bench.ApproachWorld(seat), 0.3f);
            if (!walkArrived || !bench)
            {
                yield return WalkTo(departure, 0.5f);
                Finish();
                yield break;
            }

            SetStationary(true);
            yield return Face(transform.position + bench.Facing);

            // The clip does the sitting; the root only has to be where the clip expects it.
            // SeatWorld names the seat SURFACE, and the authored pose puts the contact patch
            // SitContactHeight above the root - so the root goes that far below the slats.
            // Retargeting scales the pose by humanScale, which is what carries a child rig:
            // it ends up lifted, sitting on the bench with its feet off the pavement, instead
            // of sunk through the slats to adult depth.
            SetActivity(PedestrianAnimation.Sit);
            var seatPos = bench.SeatWorld(seat);
            seatPos.y -= PedestrianAnimation.SitContactHeight * animator.humanScale;

            // Glide over the clip's descent rather than in a third of it - the pose and the
            // root then arrive together instead of the root landing while the body is still
            // visibly on the way down. Smoothed, because a linear slide into a settling pose
            // is exactly what reads as gliding.
            var from = transform.position;
            for (var t = 0f; t < PedestrianAnimation.SitDescentSeconds; t += Time.deltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / PedestrianAnimation.SitDescentSeconds);
                transform.position = Vector3.Lerp(from, seatPos, k);
                yield return null;
            }
            transform.position = seatPos;
            // The glide moved the body without a probe, and only probes re-bucket - tell the
            // registry's spatial hash where this body now sits.
            PedestrianRegistry.Rebucket(body, seatPos);

            yield return new WaitForSeconds(Range(config.sitDurationRange));

            // Standing up puts the feet back under the root, so a rig that had to be lifted
            // onto the bench has to come back down with the pose. Free for an adult, whose
            // seat root was already on the pavement.
            SetActivity(PedestrianAnimation.None);
            var rise = seatPos;
            for (var t = 0f; t < SitTransitionSeconds; t += Time.deltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / SitTransitionSeconds);
                transform.position = new Vector3(rise.x, Mathf.Lerp(rise.y, from.y, k), rise.z);
                yield return null;
            }

            ReleaseSeat();
            SetStationary(false);
            yield return WalkTo(departure, 0.5f);

            Finish();
        }

        IEnumerator ShopRoutine(ShopEntrance shop)
        {
            Halt();
            departure = transform.position;

            yield return WalkTo(shop.StandWorld, 0.35f);
            if (!walkArrived || !shop)
            {
                yield return WalkTo(departure, 0.5f);
                Finish();
                yield break;
            }

            // The last step to the door ignores failure - a crowd on the doorstep just means
            // going in from a little further out.
            yield return WalkTo(shop.DoorWorld, 0.3f, 4f);

            SetHidden(true);
            SetStationary(true);

            yield return new WaitForSeconds(Range(config.shopDurationRange));

            // Do not materialise inside somebody browsing the shopfront.
            for (var waited = 0f; waited < 10f && !PedestrianRegistry.IsClear(body, transform.position); waited += 0.5f)
                yield return ReappearPoll;

            SetHidden(false);
            SetStationary(false);
            yield return Face(transform.position + shop.Facing);
            yield return WalkTo(departure, 0.5f);

            Finish();
        }

        /// <summary>
        /// Go into a building, stay a while, and come back out - usually somewhere else. This
        /// is what makes the city's people look like they LIVE here rather than having
        /// materialised on the pavement.
        ///
        /// It is deliberately not a spawn and a despawn. At crowd scale a Destroy/Instantiate
        /// round trip per visit would be dozens of instantiations a second, and it would throw
        /// away this walker's registry entry, LOD registration and animator wiring only to
        /// rebuild all three. Hiding costs a renderer flag; the walk out of a different door
        /// is a transform move plus a re-bucket. What the player sees is identical.
        ///
        /// The one thing that cannot be reused is the off-graph contract (walk back to
        /// departure so HumanBehavior's stale targetPoint stays valid) - somebody who comes
        /// out three streets away has no departure to walk back to. HumanBehavior.ResetRoute
        /// is the patch that covers it.
        /// </summary>
        IEnumerator BuildingRoutine(BuildingDoor door)
        {
            Halt();
            departure = transform.position;

            yield return WalkTo(door.StandWorld, 0.35f);
            if (!walkArrived || !door)
            {
                yield return WalkTo(departure, 0.5f);
                Finish();
                yield break;
            }

            // The last step through the doorway ignores failure, like the shop's does: a knot
            // of people on the pavement just means going in from a little further out.
            yield return Face(door.DoorWorld);
            yield return WalkTo(door.DoorWorld, 0.3f, 4f);

            SetHidden(true);
            SetStationary(true);

            // Inside now, so the doorstep is somebody else's to use. Holding it for the whole
            // visit would padlock the building for two minutes at a time.
            insideOf = door;
            door.Entered();
            ReleaseDoor();

            yield return new WaitForSeconds(Range(config.buildingStayRange));

            var exit = PickExit(door);

            if (!exit)
            {
                // The building was destroyed while we were inside it - a city rebuilt in Play.
                // Come back out where we stand and let the follower find a route from there.
                SetHidden(false);
                SetStationary(false);
                LeaveBuilding();
                Finish();
                if (human)
                    human.ResetRoute();
                yield break;
            }

            if (exit != door)
            {
                // The second place in the codebase that moves a body without probing - the
                // bench sit-glide is the other - so the spatial hash has to be told by hand.
                transform.position = exit.DoorWorld;
                PedestrianRegistry.Rebucket(body, transform.position);
            }

            // Do not materialise inside somebody standing on the step.
            for (var waited = 0f; waited < 10f && !PedestrianRegistry.IsClear(body, transform.position); waited += 0.5f)
                yield return ReappearPoll;

            SetHidden(false);
            SetStationary(false);
            LeaveBuilding();

            yield return Face(transform.position + exit.Facing);
            yield return WalkTo(exit.StandWorld, 0.4f);

            ReleaseDoor();
            Finish();

            // Only after Finish, which is what re-enables the follower: its route belongs to
            // wherever this walker went in, and it has to be thrown away whether or not the
            // exit was a different door - the doorstep is not where it left the pavement.
            if (human)
                human.ResetRoute();
        }

        /// <summary>
        /// Which door to come back out of. Mostly a different one somewhere else in the city -
        /// that is what stops every door from being an airlock that returns the same person -
        /// but always with a fallback to the way in, which can never fail and so can never
        /// leave somebody stuck indoors.
        /// </summary>
        BuildingDoor PickExit(BuildingDoor entered)
        {
            var director = PedestrianInteractionDirector.Instance;

            if (director && rng.NextDouble() < config.buildingSwapChance
                && director.TryPickAnyDoor(rng, out var elsewhere)
                && elsewhere && elsewhere != entered
                && elsewhere.TryClaimStep())
            {
                claimedDoor = elsewhere;
                return elsewhere;
            }

            // Back out the way we came. The step was released on going in, so somebody may be
            // standing on it - stepping out onto an occupied doorstep is what the avoidance
            // layer is for. Only record a claim we actually won: releasing one we did not
            // would hand somebody else's doorstep away.
            if (entered && entered.TryClaimStep())
                claimedDoor = entered;

            return entered;
        }

        IEnumerator IdleRoutine()
        {
            Halt();
            SetStationary(true);
            yield return new WaitForSeconds(Range(config.idleDurationRange));
            Finish();
        }

        // ------------------------------------------------------------------ movement

        /// <summary>
        /// The off-graph walk: straight toward <paramref name="target"/> through the same
        /// Probe/Blend/clamp loop as the patched follower, so detouring people steer around
        /// each other and can never overlap. Sets <see cref="walkArrived"/> for callers that
        /// need to know whether to go through with the activity at the far end.
        /// </summary>
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
            walkArrived = PedestrianSteering.Flat(target - transform.position).magnitude <= stopWithin * 2f + 0.5f;
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

        /// <summary>Take the transform over from HumanBehavior - the PedestrianIdler contract.</summary>
        void Halt()
        {
            human.enabled = false;
            body.SpeedMs = 0f;
            SetSpeed(0f);
        }

        void Finish()
        {
            Restore();
            activity = null;
            cooldownUntil = Time.time + Range(config.interactionCooldownRange);
        }

        /// <summary>Every hold this component can have on the pedestrian, released. Idempotent.</summary>
        void Restore()
        {
            ReleaseSeat();
            ReleaseDoor();
            LeaveBuilding();
            SetActivity(PedestrianAnimation.None);
            SetHidden(false);
            SetStationary(false);
            if (human)
                human.enabled = true;
        }

        void ReleaseSeat()
        {
            if (claimedBench)
                claimedBench.Release(claimedSeat);
            claimedBench = null;
            claimedSeat = -1;
        }

        void ReleaseDoor()
        {
            if (claimedDoor)
                claimedDoor.ReleaseStep();
            claimedDoor = null;
        }

        /// <summary>
        /// Come off a building's occupancy count. Separate from the doorstep claim because the
        /// two have different lifetimes: the step is held for seconds at each end of a visit,
        /// the occupancy for the whole of it.
        /// </summary>
        void LeaveBuilding()
        {
            if (insideOf)
                insideOf.Left();
            insideOf = null;
        }

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

        void SetActivity(int value)
        {
            if (hasActivityParam)
                animator.SetInteger(PedestrianAnimation.ActivityHash, value);
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
