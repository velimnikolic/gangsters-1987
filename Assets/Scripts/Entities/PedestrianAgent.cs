using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;

namespace LivingCity.Entities
{
    /// <summary>
    /// The interaction brain riding on every spawned pedestrian: chats and arguments (on the
    /// director's command), bench sits, shop visits and plain idling (rolled locally when an
    /// opportunity is nearby). Subsumes PedestrianIdler.
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

        /// <summary>Seconds between opportunity rolls while walking free.</summary>
        const float RollInterval = 2f;

        /// <summary>How far off the pavement a walker will detour for a bench or a door.</summary>
        const float OpportunityRange = 9f;

        /// <summary>Give-up timer on any single off-graph leg. Generous - it is a backstop.</summary>
        const float OffGraphTimeout = 20f;

        /// <summary>Seconds the one-shot sit-down / stand-up clips are given to play out.</summary>
        const float SitTransitionSeconds = 2.4f;

        /// <summary>Matches HumanBehavior's own animator scaling (speed * 0.8).</summary>
        const float AnimatorSpeedScale = 0.8f;

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

        public bool AvailableForChat =>
            activity == null && rng != null && Time.time >= cooldownUntil
            && human && human.enabled && human.IsMoving;

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

        void Update()
        {
            if (activity != null || rng == null || body == null)
                return;
            if (Time.time < cooldownUntil || Time.time < nextRollAt)
                return;

            nextRollAt = Time.time + RollInterval;

            if (!human.enabled || !human.IsMoving)
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

            // Scaled down because this needs no nearby prop: unscaled, a 0.3 chance per roll
            // would have everybody stopping within seconds of their cooldown expiring.
            if (rng.NextDouble() < config.idleChance * 0.2f)
                Begin(IdleRoutine());
        }

        /// <summary>Director's command. Both members of the pair receive it on the same tick.</summary>
        public void BeginConversation(Vector3 partnerPos, float duration, bool argue)
        {
            if (activity != null || rng == null)
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
            var midpoint = (transform.position + partnerPos) * 0.5f;
            yield return WalkTo(midpoint, argue ? 0.5f : 0.7f, 4f);

            SetStationary(true);
            yield return Face(partnerPos);

            SetActivity(argue ? PedestrianAnimation.Argue : PedestrianAnimation.Talk);
            yield return new WaitForSeconds(duration);
            SetActivity(PedestrianAnimation.None);

            // A beat for the exit transition before the walk cycle takes over.
            yield return new WaitForSeconds(0.4f);

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

            // The sit-down clip carries the hips back and down onto the slats; the root only
            // has to glide the last half-metre to the authored seat point while it plays.
            SetActivity(PedestrianAnimation.Sit);
            var from = transform.position;
            var seatPos = bench.SeatWorld(seat);
            for (var t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(from, seatPos, t / 0.6f);
                yield return null;
            }
            transform.position = seatPos;

            yield return new WaitForSeconds(Range(config.sitDurationRange));

            SetActivity(PedestrianAnimation.None);
            yield return new WaitForSeconds(SitTransitionSeconds);

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
                yield return new WaitForSeconds(0.5f);

            SetHidden(false);
            SetStationary(false);
            yield return Face(transform.position + shop.Facing);
            yield return WalkTo(departure, 0.5f);

            Finish();
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

                yield return new WaitForFixedUpdate();
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
