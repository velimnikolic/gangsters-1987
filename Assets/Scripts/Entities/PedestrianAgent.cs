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
    public sealed class PedestrianAgent : MonoBehaviour, UI.IOverlaySubject
    {
        /// <summary>
        /// What this walker is doing, as a fact the popup can read. The activities
        /// themselves are coroutines - this is set at the top of each and cleared in
        /// Restore(), the one choke point every exit path already runs through, so no
        /// coroutine can leak a stale state. Every value must have a sentence and a colour
        /// in PedestrianIntention; the headless suite asserts it.
        /// </summary>
        public enum Activity
        {
            Strolling, Chatting, Arguing, Sitting, Shopping, Visiting, Idling,

            // The daily routine's states. CommutingToWork/HeadingHome/AtHome are set by the
            // commute routine; AtWork and EveningStroll exist only in EffectiveActivity -
            // schedule flavour over plain strolling, never stored in Current.
            CommutingToWork, AtWork, HeadingHome, AtHome, EveningStroll,

            Dead,
        }

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

        /// <summary>Give-up timer on the step off the carriageway. Short: it is a metre or two.</summary>
        const float RoadClearTimeout = 4f;

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
        PedestrianDeath death;
        bool hasSpeedParam;
        bool hasActivityParam;

        // Identity: ints at spawn, a string only on the first popup read. Derived from the
        // seed by hashing, never from the live rng - see PedestrianIdentity.
        int identitySeed;
        bool female;
        PedestrianOccupation occupation;
        string title;

        /// <summary>Which errand the current shop visit is - an index into
        /// PedestrianIntention.Errands, varied per visit without touching the rng stream.</summary>
        int errandIndex;
        int shopVisits;

        // The daily routine. Home and workplace are doors picked by hash off the
        // position-sorted door array - lazy, and re-resolved if the city is rebuilt.
        // The latches are per-day, the SchoolBusAgent pattern: a window is served once
        // and re-arms at midnight, however fast the clock runs.
        BuildingDoor homeDoor;
        BuildingDoor workDoor;
        float scheduleOffset;
        bool worksDays;
        int commutedDay = -1;
        int homewardDay = -1;

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

        public void Configure(CityConfig cityConfig, int seed,
                              string groupLabel = null, string prefabName = null)
        {
            config = cityConfig;
            rng = new System.Random(seed);

            identitySeed = seed;
            female = PedestrianIdentity.IsFemale(prefabName);
            occupation = PedestrianIdentity.Occupation(groupLabel, prefabName, female, seed);

            // Children have school (its own system), the fringe have nowhere to be.
            worksDays = occupation != PedestrianOccupation.Schoolkid
                     && occupation != PedestrianOccupation.Drifter
                     && occupation != PedestrianOccupation.Unemployed
                     && occupation != PedestrianOccupation.Punk;
            scheduleOffset = PedestrianSchedule.OffsetHours(seed, config.commuteStaggerHours);

            // Staggered grace period so a freshly spawned crowd does not all decide to
            // perform in the same second.
            cooldownUntil = Time.time + Range(new Vector2(5f, config.interactionCooldownRange.y * 0.5f));
        }

        /// <summary>What this walker is doing right now. Written by the activity routines.</summary>
        public Activity Current { get; private set; } = Activity.Strolling;

        /// <summary>
        /// Current, with death and the day's phase folded in. A corpse reports Dead
        /// whatever came before (death disables this component, but property reads still
        /// work). Plain strolling is flavoured by the schedule - "on the clock", "out for
        /// the evening" - which is how every coroutine path gets routine-aware words
        /// without any routine touching them: they all Finish() back into Strolling and
        /// the phase does the rest. Read only for the selected subject.
        /// </summary>
        Activity EffectiveActivity
        {
            get
            {
                if (death && death.IsDead)
                    return Activity.Dead;
                if (Current != Activity.Strolling)
                    return Current;

                var director = PedestrianInteractionDirector.Instance;
                if (!director || !director.HasClock || config == null)
                    return Activity.Strolling;

                return PhaseNow(director) switch
                {
                    DayPhase.MorningCommute when worksDays =>
                        commutedDay == director.DayNow ? Activity.AtWork : Activity.CommutingToWork,
                    DayPhase.WorkDay when worksDays => Activity.AtWork,
                    DayPhase.EveningCommute when worksDays =>
                        homewardDay == director.DayNow ? Activity.EveningStroll : Activity.HeadingHome,
                    DayPhase.EveningOut => Activity.EveningStroll,
                    // Still on the street after the night hour: honestly, trying to get home.
                    DayPhase.AtHomeNight => Activity.HeadingHome,
                    _ => Activity.Strolling,
                };
            }
        }

        DayPhase PhaseNow(PedestrianInteractionDirector director) =>
            PedestrianSchedule.PhaseFor(director.HourNow, scheduleOffset, worksDays,
                config.workMorningHour, config.workEveningHour, config.nightHomeHour);

        void Awake()
        {
            human = GetComponent<HumanBehavior>();
            animator = GetComponent<Animator>();
            renderers = GetComponentsInChildren<Renderer>(true);

            // Added by the spawner before this component (InteractableNpc pulls it in), so
            // it is there to find - but never assumed: a bare prefab has no death to report.
            death = GetComponent<PedestrianDeath>();
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
            if (Time.time < nextRollAt)
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

            // The clock outranks both the dice and the after-activity cooldown: a commute
            // window is a duty, not a leisure roll, and a 90s cooldown would eat most of
            // one. Ordered before the cooldown gate on purpose; the leisure rolls below
            // keep their old pacing exactly.
            if (director && TickSchedule(director))
                return;

            if (Time.time < cooldownUntil)
                return;

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

        /// <summary>
        /// The daily-routine check, living inside the director's sliced roll so an hour
        /// boundary can never move the whole city on one frame. The commute windows carry
        /// per-day latches (the SchoolBusAgent pattern - edge-triggered, tolerant of any
        /// clock speed); the night has none, because "get indoors" should retry every roll
        /// until it lands, and ends naturally when the morning exit happens. A window that
        /// closes unserved is simply surrendered - EffectiveActivity carries the intention
        /// as words over ordinary strolling, so the popup stays honest and nobody sticks.
        /// </summary>
        bool TickSchedule(PedestrianInteractionDirector director)
        {
            if (!director.HasClock || config == null)
                return false;

            var phase = PhaseNow(director);

            // The night branch alone respects the cooldown: it is level-triggered and
            // retries for hours, so the delay is invisible - and at a pre-dawn startHour
            // the whole fresh crowd is already "up past bedtime", where the staggered
            // spawn cooldown is the only thing spreading two hundred simultaneous door
            // trips over the first minute of Play.
            if (phase == DayPhase.AtHomeNight
                && Time.time >= cooldownUntil
                && TryStartCommute(ResolveHomeDoor(director), Activity.HeadingHome, night: true))
                return true;

            if (worksDays && phase == DayPhase.MorningCommute && commutedDay != director.DayNow
                && TryStartCommute(ResolveWorkDoor(director), Activity.CommutingToWork, night: false))
            {
                commutedDay = director.DayNow;
                return true;
            }

            if (worksDays && phase == DayPhase.EveningCommute && homewardDay != director.DayNow
                && TryStartCommute(ResolveHomeDoor(director), Activity.HeadingHome, night: false))
            {
                homewardDay = director.DayNow;
                return true;
            }

            return false;
        }

        /// <summary>
        /// A commute is a building visit wearing a purpose: enter the nearest street door -
        /// the same 9m detour every visit already pays, because no city-wide pedestrian
        /// pathfinding exists and none is needed - and come out at the assigned door across
        /// town. False when no door is in reach or its step is taken; the next roll
        /// retries for as long as the phase lasts.
        /// </summary>
        bool TryStartCommute(BuildingDoor destination, Activity intent, bool night)
        {
            var director = PedestrianInteractionDirector.Instance;
            if (!director || !destination)
                return false;

            if (!director.TryPickDoor(transform.position, OpportunityRange, out var entry)
                || !entry.TryClaimStep())
                return false;

            claimedDoor = entry;
            Begin(BuildingRoutine(entry, destination, intent, night));
            return true;
        }

        // Lazy and re-resolved when a rebuilt city destroys the old door. The multipliers
        // key the two draws apart, the way PedestrianIdentity's salts do.
        BuildingDoor ResolveHomeDoor(PedestrianInteractionDirector director)
        {
            if (!homeDoor)
                homeDoor = director.DoorByHash(unchecked(identitySeed * 40503 + 7));
            return homeDoor;
        }

        BuildingDoor ResolveWorkDoor(PedestrianInteractionDirector director)
        {
            if (!workDoor)
                workDoor = director.DoorByHash(unchecked(identitySeed * 69069 + 13));
            return workDoor;
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
            // Set at the top of every routine, so the approach legs read as part of the
            // activity - walking over to a partner IS the chat, to the popup's eye.
            Current = argue ? Activity.Arguing : Activity.Chatting;
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
            Current = Activity.Sitting;
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
            Current = Activity.Shopping;
            // A fresh errand per visit, hashed rather than drawn - the rng stream is part
            // of the crowd's determinism contract and this must not consume from it.
            errandIndex = (identitySeed ^ shopVisits++) & 7;
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
        IEnumerator BuildingRoutine(BuildingDoor door) =>
            BuildingRoutine(door, null, Activity.Visiting, night: false);

        /// <summary>
        /// The parameterised heart the commute reuses: <paramref name="forcedExit"/> makes
        /// "come out somewhere else" into "come out at YOUR door" - home in the evening,
        /// work in the morning - and <paramref name="night"/> swaps the drawn stay for
        /// staying in until the personal morning, which is what empties the streets after
        /// the night hour and refills them at dawn.
        /// </summary>
        IEnumerator BuildingRoutine(BuildingDoor door, BuildingDoor forcedExit,
                                    Activity intent, bool night)
        {
            Current = intent;
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

            if (night)
            {
                // Indoors until the personal morning: a phase equality on a slow poll,
                // never a duration - the clock can be scrubbed or run at any speed and
                // the exit still lands in the right window (or skips a night entirely).
                Current = Activity.AtHome;
                var director = PedestrianInteractionDirector.Instance;

                if (director && director.HasClock)
                    while (director && director.HasClock && PhaseNow(director) == DayPhase.AtHomeNight)
                        yield return ReappearPoll;
                else
                    yield return new WaitForSeconds(Range(config.buildingStayRange));

                // A worker steps from the doorstep straight into the day's first duty -
                // the morning exit IS the commute, no second building trip to serve.
                if (worksDays && director && director.HasClock
                    && PhaseNow(director) == DayPhase.MorningCommute)
                {
                    var work = ResolveWorkDoor(director);
                    if (work)
                        forcedExit = work;
                    commutedDay = director.DayNow;
                    Current = Activity.CommutingToWork;
                }
            }
            else
            {
                yield return new WaitForSeconds(Range(config.buildingStayRange));
            }

            // The forced door first - that is the whole commute - with the random pick as
            // the fallback for a door that was destroyed or whose step never frees.
            var exit = ForceExit(forcedExit);
            if (!exit)
                exit = PickExit(door);

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
        /// <summary>The commute's exit: the assigned door, claimed like any other. Null -
        /// never a destroyed reference - when there is nothing to force or the step is
        /// taken, so the caller's fallback test stays a plain truthiness check.</summary>
        BuildingDoor ForceExit(BuildingDoor forced)
        {
            if (!forced || !forced.TryClaimStep())
                return null;

            claimedDoor = forced;
            return forced;
        }

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
            Current = Activity.Idling;
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
        IEnumerator WalkTo(Vector3 target, float stopWithin, float timeout = OffGraphTimeout,
                           bool clearing = false)
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

            // PATCH (Living City): an off-graph leg can end on the carriageway - a bench or a
            // door within OpportunityRange can be across a street, and this walk gives up
            // wherever it ran out of time. Whatever the caller does next (talk, sit, hand back to
            // the follower) it does STANDING STILL, and standing still on a crossing holds the
            // car that stopped for it. So step clear first; the caller's verdict on its own
            // target survives, because SitRoutine and friends decide on it.
            if (!clearing && RoadSurface.TryNearestOffRoad(transform.position, out var clear))
            {
                var arrived = walkArrived;
                yield return WalkTo(clear, 0.3f, RoadClearTimeout, clearing: true);
                walkArrived = arrived;
            }
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
            Current = Activity.Strolling;
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

        // --------------------------------------------------------------- the overlay
        // Explicit implementation - the HUD's plumbing, not the agent's own API.
        //
        // Civilians are the one subject type that NEVER registers in OverlayRegistry: at
        // crowd scale (the design target is ten thousand) a marker Image each would have
        // SyncMarkers destroying and rebuilding the lot every time a bank customer spawns.
        // Picking is physics-based and the HUD builds one ephemeral marker for an
        // off-registry selection, so a civilian is exactly as clickable as a registered
        // subject and costs the overlay nothing until clicked.

        Transform UI.IOverlaySubject.OverlayAnchor => transform;
        float UI.IOverlaySubject.OverlayHeight => 2.2f;

        /// <summary>Inside a shop or a building: still a live subject, just nowhere to
        /// point at - and not pickable, which keeps the doorway click a move order.</summary>
        bool UI.IOverlaySubject.OverlayHidden => body != null && body.Hidden;

        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Diamond;
        Color UI.IOverlaySubject.OverlayColor => UI.PedestrianIntention.ActivityColor(EffectiveActivity);
        string UI.IOverlaySubject.OverlayTitle =>
            title ??= PedestrianIdentity.ComposeTitle(identitySeed, female, occupation);
        string UI.IOverlaySubject.OverlayLine => UI.PedestrianIntention.Line(EffectiveActivity, errandIndex);

        /// <summary>Errand above the activity - changes exactly when the popup's words
        /// would. EffectiveActivity already folds in death and the day's phase, so a
        /// selected walker's popup rewrites itself as the schedule turns over.</summary>
        long UI.IOverlaySubject.OverlayKey =>
            ((long)errandIndex << 16) | (long)EffectiveActivity;

        // ---------------------------------------------------------------------------------

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
