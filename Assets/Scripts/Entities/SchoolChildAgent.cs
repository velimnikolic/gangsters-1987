using System.Collections;
using UnityEngine;
using LivingCity.City;

namespace LivingCity.Entities
{
    /// <summary>
    /// One schoolchild, for the whole of its life: waiting at a bus stop, riding to school,
    /// walking in through the school's door, and the same in reverse in the afternoon. Spawned
    /// and bound by SchoolBusDirector, persistent, and deliberately NOT a PedestrianAgent - a
    /// child on the school run never sits on a bench, never shops, never wanders into a
    /// stranger's building and never joins the chat pairing, and staying out of
    /// PedestrianAgent.Agents is the whole opt-out, exactly as it is for a beat officer.
    ///
    /// Between runs the children simply STAND at their stop. That is not laziness about the
    /// off-hours: the alternative is hiding them at home and materialising them at a kerb when
    /// the bus is due, and a person popping into existence on a pavement is the single most
    /// artificial thing this project has spent its effort avoiding. A child waiting a long time
    /// for a bus is, if anything, the more period-accurate of the two.
    ///
    /// The off-graph legs (to the bus door, along the school's facade, through the door) reuse
    /// the WalkTo/Face loop from PedestrianAgent VERBATIM, via PoliceOfficerAgent, rather than
    /// by refactor. That class is the 10k-crowd hot path and this is the third consumer of the
    /// loop - the point at which its own comment says to extract. It is still copied, because
    /// what the three consumers share is thirty lines of arithmetic while what they differ in
    /// is every line around it, and a base class here would be inheritance used as a clipboard.
    ///
    /// Crowd invariants honoured: the body registers once for the object's lifetime; every move
    /// goes through Probe/Blend and the AllowedAdvance clamp; Hidden and Stationary are kept
    /// truthful; a teleport is always followed by Rebucket; re-paths flow through
    /// PedestrianRepathQueue via HumanBehavior; no per-instance Update.
    /// </summary>
    [RequireComponent(typeof(HumanBehavior))]
    public sealed class SchoolChildAgent : MonoBehaviour, UI.IOverlaySubject
    {
        /// <summary>Public because the overlay HUD colours the marker and words the popup off
        /// it. The transitions stay this component's alone.</summary>
        public enum State { Waiting, Boarding, Riding, Alighting, InSchool, WalkingToSchool }

        /// <summary>Metres above the child's origin to float the marker. Lower than the
        /// officer's 2.3 because a child is shorter, and because a queue of them at a stop
        /// would otherwise be a row of markers floating over nothing.</summary>
        const float MarkerHeight = 1.9f;

        /// <summary>How near the school a walking child must get before the off-graph legs take
        /// over. The officer's number for the officer's reason: a sidewalk route's A*
        /// destination resolves to the nearest TILE, so a genuine arrival is within one.</summary>
        const float ArriveRange = 20f;

        /// <summary>Give-up timer on any single off-graph leg - the PedestrianAgent value.</summary>
        const float OffGraphTimeout = 20f;

        /// <summary>Matches HumanBehavior's own animator scaling (speed * 0.8).</summary>
        const float AnimatorSpeedScale = 0.8f;

        static readonly WaitForSeconds ClearPoll = new WaitForSeconds(0.5f);
        static readonly WaitForSeconds WalkPoll = new WaitForSeconds(0.5f);
        static readonly WaitForFixedUpdate FixedStep = new WaitForFixedUpdate();

        SchoolBusDirector director;

        HumanBehavior human;
        Animator animator;
        Renderer[] renderers;
        Collider[] colliders;
        PedestrianBody body;
        bool hasSpeedParam;

        State state;
        SchoolBusAgent bus;

        /// <summary>The one activity running right now. Same single-slot discipline as
        /// PedestrianAgent: a new order replaces the old one rather than racing it.</summary>
        Coroutine activity;

        /// <summary>Which stop this child belongs to. Fixed for life - it is where they live,
        /// and it is what makes the afternoon run put everybody back where they came from.</summary>
        public int HomeStop { get; private set; }

        public State CurrentState => state;

        /// <summary>Standing at a stop, free to be told to get on. Kept separate from the one
        /// below because WHERE a child is waiting decides which half of the day can collect
        /// them: a child at a kerb must not be summoned to the school's door across half the
        /// city, and a child inside the school must not be counted at a kerb it is nowhere
        /// near. One "can board" flag conflated the two and sent both on the wrong walk.</summary>
        public bool CanBoardAtStop => state == State.Waiting;

        /// <summary>Inside the school, waiting for the run home.</summary>
        public bool CanBoardAtSchool => state == State.InSchool;

        public bool Boarding => state == State.Boarding;
        public bool Riding => state == State.Riding;
        public bool Alighting => state == State.Alighting;

        void Awake()
        {
            human = GetComponent<HumanBehavior>();
            animator = GetComponent<Animator>();
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
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

        void OnDestroy()
        {
            UI.OverlayRegistry.Unregister(this);
            PedestrianRegistry.Unregister(body);
            body = null;
        }

        // --------------------------------------------------------------- the overlay
        // Explicit implementation - the HUD's plumbing, not the child's own API.

        Transform UI.IOverlaySubject.OverlayAnchor => transform;
        float UI.IOverlaySubject.OverlayHeight => MarkerHeight;

        /// <summary>Riding inside the bus or sat in class: still a live subject, just not
        /// anywhere to point at. Read off the body rather than off the state, because IsVisible
        /// is what actually decides whether there is a child on screen.</summary>
        bool UI.IOverlaySubject.OverlayHidden => !IsVisible;

        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Diamond;
        Color UI.IOverlaySubject.OverlayColor => UI.SchoolIntention.ChildColor(state);
        string UI.IOverlaySubject.OverlayTitle => UI.SchoolIntention.ChildTitle(Number);
        string UI.IOverlaySubject.OverlayLine => UI.SchoolIntention.ChildIntention(state);
        long UI.IOverlaySubject.OverlayKey => (long)state;

        // ---------------------------------------------------------------------------------

        /// <summary>1-based, set by the director - "Schoolchild 3" on the popup.</summary>
        public int Number { get; private set; }

        public void Bind(SchoolBusDirector owner, int stop, int number)
        {
            director = owner;
            HomeStop = stop;
            Number = number;
            UI.OverlayRegistry.Register(this);

            // Standing at the stop with the follower off. Nothing here wanders: the child is
            // either waiting, being driven by a coroutine, or hidden.
            human.enabled = false;
            human.randomDestination = false;
            state = State.Waiting;
            SetStationary(true);
        }

        // ------------------------------------------------------------------ orders

        /// <summary>
        /// Get on the bus. <paramref name="order"/> is this child's place in the boarding
        /// order, which becomes a delay rather than a turn in a queue - see
        /// SchoolRun.BoardingStagger for why a semaphore would be the wrong shape.
        /// </summary>
        public void BeginBoarding(SchoolBusAgent vehicle, int order)
        {
            if (!CanBoardAtStop && !CanBoardAtSchool)
                return;

            bus = vehicle;
            state = State.Boarding;
            Begin(BoardRoutine(order));
        }

        /// <summary>Get off at this stop and go back to waiting for tomorrow.</summary>
        public void BeginAlighting(SchoolBusAgent vehicle, int order, Vector3 queuePoint)
        {
            if (state != State.Riding)
                return;

            bus = vehicle;
            state = State.Alighting;
            var slot = SchoolRun.QueueSlot(order, queuePoint, -bus.transform.forward);
            Begin(AlightRoutine(order, slot, enterSchool: false));
        }

        /// <summary>Get off at the school and go in through its door.</summary>
        public void BeginSchoolArrival(SchoolBusAgent vehicle, int order, int count)
        {
            if (state != State.Riding)
                return;

            bus = vehicle;
            state = State.Alighting;

            var school = director.School;
            var stand = school
                ? SchoolRun.FanSlot(order, count, school.StandWorld, school.Tangent)
                : transform.position;

            Begin(AlightRoutine(order, stand, enterSchool: true));
        }

        /// <summary>
        /// The bus has run out of dwell and left without this child. Walk instead - the whole
        /// point of the cap is that missing the bus is a visible outcome rather than a stuck
        /// one, and this is also what happens if the bus never came at all.
        /// </summary>
        public void GiveUpAndWalk()
        {
            if (state != State.Boarding)
                return;

            // A child who was inside the school and failed to get on the bus home simply stays
            // inside; there is nothing to walk to and nothing to see.
            if (!director.School)
            {
                state = State.Waiting;
                return;
            }

            state = State.WalkingToSchool;
            Begin(WalkToSchoolRoutine());
        }

        // ------------------------------------------------------------------ routines

        IEnumerator BoardRoutine(int order)
        {
            // Everybody starts a beat apart. The avoidance clamp does the rest, exactly the way
            // it already resolves a crowded doorstep.
            yield return new WaitForSeconds(order * SchoolRun.BoardingStagger);

            var wasInside = !IsVisible;
            if (wasInside)
            {
                // Coming out of the school for the run home. Do not materialise inside somebody
                // standing on the step - the officer's reappear poll, for the officer's reason.
                var school = director.School;
                if (school)
                {
                    transform.position = school.DoorWorld;
                    PedestrianRegistry.Rebucket(body, transform.position);
                }

                for (var waited = 0f;
                     waited < 5f && !PedestrianRegistry.IsClear(body, transform.position);
                     waited += 0.5f)
                    yield return ClearPoll;

                SetHidden(false);
            }

            if (!bus)
            {
                state = State.Waiting;
                yield break;
            }

            var door = bus.DoorWorld;
            yield return Face(door);
            yield return WalkTo(door, 0.6f);

            // Arrival is not checked. A child who could not quite reach the door in time is
            // still standing next to a bus that is about to leave, and the honest outcome is
            // the same either way: the dwell cap decides, not this walk.
            if (!bus || state != State.Boarding)
                yield break;

            Embark();
        }

        /// <summary>
        /// Aboard. The order matters, and the third step is the one that is easy to miss.
        /// </summary>
        void Embark()
        {
            SetHidden(true);
            SetStationary(true);

            // Hidden means something to PedestrianRegistry and NOTHING to PhysX. Crosswalk
            // counts every collider tagged "Human" that has moved since its last sample, so a
            // hidden child carried through a crosswalk trigger by the bus reads as somebody
            // crossing the road - and stops every car holding for that crossing, the bus it is
            // riding in included.
            SetCollidersEnabled(false);

            transform.SetParent(bus.transform, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            state = State.Riding;
        }

        IEnumerator AlightRoutine(int order, Vector3 target, bool enterSchool)
        {
            yield return new WaitForSeconds(order * SchoolRun.BoardingStagger);

            var step = bus ? bus.DoorWorld : target;

            transform.SetParent(director.transform, worldPositionStays: false);
            transform.SetPositionAndRotation(step, Quaternion.LookRotation(
                Flat(target - step).sqrMagnitude > 1e-4f ? Flat(target - step) : transform.forward,
                Vector3.up));

            // Mandatory after any move that did not go through Probe: the body's hash cell is
            // still the one it had when it got on the bus.
            PedestrianRegistry.Rebucket(body, transform.position);

            SetCollidersEnabled(true);

            for (var waited = 0f;
                 waited < 5f && !PedestrianRegistry.IsClear(body, transform.position);
                 waited += 0.5f)
                yield return ClearPoll;

            SetHidden(false);
            SetStationary(false);

            yield return WalkTo(target, 0.4f);

            if (enterSchool)
            {
                yield return EnterSchool();
                yield break;
            }

            SetStationary(true);
            state = State.Waiting;
        }

        /// <summary>The last few metres of the morning: face the door, walk into it, vanish.
        /// BuildingRoutine's choreography against the school's own anchors.</summary>
        IEnumerator EnterSchool()
        {
            var school = director.School;
            if (!school)
            {
                SetStationary(true);
                state = State.Waiting;
                yield break;
            }

            yield return Face(school.DoorWorld);
            yield return WalkTo(school.DoorWorld, 0.3f, 6f);

            SetHidden(true);
            SetStationary(true);
            state = State.InSchool;
        }

        /// <summary>
        /// Missed the bus. Walk to school on the pavement graph like anybody else - this is the
        /// one leg long enough to need real pathfinding rather than the off-graph loop.
        /// </summary>
        IEnumerator WalkToSchoolRoutine()
        {
            var school = director.School;

            SetStationary(false);
            human.randomDestination = true;
            human.hasScriptedDestination = true;
            human.scriptedDestination = school.DoorWorld;
            human.enabled = true;

            // One frame's grace so Start can run - it ignores the scripted destination and would
            // otherwise overwrite the route this asks for. ResetRoute then re-paths through the
            // ordinary budget queue, and Repath is the call that honours the override.
            yield return null;
            human.ResetRoute();

            // No deadline. A path can fail and HumanBehavior retries it on its own timer; a
            // child standing halfway to school is a child standing on a pavement, which is the
            // least-bad thing that can happen here and needs no rescue.
            while (Flat(transform.position - school.StandWorld).sqrMagnitude >
                   ArriveRange * ArriveRange)
                yield return WalkPoll;

            // Disabling the follower is the take-over the patch was built for - MoveToNextPoint
            // sees enabled false and never enqueues.
            human.enabled = false;
            human.hasScriptedDestination = false;

            yield return WalkTo(school.StandWorld, 0.5f);
            yield return EnterSchool();
        }

        void Begin(IEnumerator routine)
        {
            if (activity != null)
                StopCoroutine(activity);

            activity = StartCoroutine(routine);
        }

        // ------------------------------------------------------------------ movement
        // Copied from PedestrianAgent via PoliceOfficerAgent - see the class comment for why
        // copied rather than shared.

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

        bool IsVisible => body != null && !body.Hidden;

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

        void SetCollidersEnabled(bool enabled)
        {
            foreach (var c in colliders)
                if (c)
                    c.enabled = enabled;
        }

        void SetSpeed(float metresPerSecond)
        {
            if (hasSpeedParam)
                animator.SetFloat(PedestrianAnimation.SpeedHash, metresPerSecond * AnimatorSpeedScale);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

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
