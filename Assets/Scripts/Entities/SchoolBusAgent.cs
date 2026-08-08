using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;

namespace LivingCity.Entities
{
    /// <summary>
    /// The brain of the school bus, for the whole of its life. One persistent vehicle: it waits
    /// at the school, drives the stops in order collecting children, brings them back, and in
    /// the afternoon drives the same stops in reverse putting them down again. None of the
    /// traffic system's churn applies - no TrafficAgent, no exit gates, no VehicleSpawner
    /// bookkeeping - exactly as for the patrol fleet.
    ///
    /// It is PolicePatrolAgent's machinery with a different errand and one genuinely new
    /// problem: the bus does not dock off the road into a bay, it stops IN THE LANE, because
    /// that is what a bus stop is and the school - having no landmarkCars - has no forecourt to
    /// turn into. Everything below that differs from the patrol car follows from that.
    ///
    /// 1. A halt can only be claimed in the routeCompleted window. stopHere is not a brake:
    ///    CarBehavior reads it in exactly one place, immediately after announcing the end of a
    ///    trajectory, where it means "do not choose another path" (CarBehavior.cs:528). Setting
    ///    it a frame later is a frame too late - the car already has a new route.
    ///
    /// 2. The halt also sets maxspeed to 0. stopHere alone is not enough for a vehicle standing
    ///    in live road: StartMoving / CrosswalkChange / LevelCrossingChange set isMoving back to
    ///    true without consulting it (CarBehavior.cs:942-966), and a bus parked with its trigger
    ///    box inside a light it subscribed to would lurch on green. It recovers by itself - the
    ///    spent trajectory re-announces completion and stopHere stops it again - but a bus that
    ///    twitches every time a light changes is not what anyone wants to watch.
    ///
    /// 3. OnRouteCompleted is guarded, because that re-announcement happens every frame.
    ///
    /// 4. The dwell is capped hard. Nothing overtakes - the pack only leaves a lane on a
    ///    LaneChange path - so the queue behind the bus is a real queue, and once it reaches a
    ///    junction the box-blocking timeouts start dissolving it the expensive way. Any child
    ///    still on the pavement when the cap expires walks to school instead, which also covers
    ///    the case where the bus never arrived at all.
    ///
    /// CarBehavior stays ENABLED throughout, for PolicePatrolAgent's reason: disabling it
    /// unregisters the bus from TrafficRegistry, and the one thing a bus stopped in a lane must
    /// be is something traffic can see and brake for. That is also why the bus is never
    /// parked by disabling it at spawn the way a patrol car in a stall is - it rests at the
    /// school KERB, which is live road.
    ///
    /// No per-frame Update: one coroutine drives the run, and CarBehavior does the driving.
    /// </summary>
    public sealed class SchoolBusAgent : MonoBehaviour
    {
        public enum State { Waiting, Driving, Halted, Returning, Unloading }

        /// <summary>
        /// How near its target a completed route must end before the bus counts as arrived. A
        /// route's A* destination resolves to the nearest TILE, so a completed route ends
        /// within a tile length of the point it aimed at - PolicePatrolAgent's 20m accepts that
        /// and rejects a completion a re-path landed somewhere else entirely.
        ///
        /// The bus therefore stops NEAR its stop rather than on it, and the children walk the
        /// last few metres to wherever it actually is. That is the right way round: the
        /// alternative is hand-driving the bus the last 20m while CarBehavior is still writing
        /// the transform every frame, for a queue that shuffles forward anyway.
        /// </summary>
        const float ArriveRange = 20f;

        /// <summary>Metres before the target the bus starts shedding speed. Done by lowering
        /// maxspeed rather than by moving the transform, so the pack's own car-following model
        /// decelerates it at its comfortable rate and everything behind inherits the slowdown
        /// one car at a time.</summary>
        const float BrakingDistance = 25f;

        /// <summary>How slow the approach gets. Not zero - the bus still has to reach the
        /// stop - and not fast, because it is about to stand still.</summary>
        const float ApproachSpeed = 4f;

        /// <summary>Route completions accepted somewhere other than the target before the bus
        /// gives up on that stop. The officer's number for the officer's reason: an unreachable
        /// target means a severed graph, and orbiting forever is worse than skipping a stop.</summary>
        const int AimRetries = 3;

        /// <summary>Ceiling on the off-road legs at the school, where there is no lane to hold
        /// up and so no dwell cap - only a guard against a child that never reports in.</summary>
        const float SchoolLegTimeout = 30f;

        static readonly WaitForSeconds BoardPoll = new WaitForSeconds(0.25f);
        static readonly WaitForSeconds SchedulePoll = new WaitForSeconds(0.5f);

        CityConfig config;
        SchoolBusDirector director;
        Vector3 schoolKerb;
        CarBehavior car;

        readonly List<SchoolChildAgent> waiting = new();

        State state;
        float cruiseSpeed;
        Vector3 driveTarget;
        bool arrived;
        int aimAttempts;

        /// <summary>Which way the pavement lies at the current halt. The bus cannot work this
        /// out from its own transform - both flanks look the same to it - so whoever asks for
        /// a halt says where the people are.</summary>
        Vector3 pavementHint;

        /// <summary>True while a run is in flight. The clock is a TRIGGER and never a
        /// condition: at the project's own 1 real second per game hour a whole day passes in
        /// less time than one round trip takes, so a run that re-checked its window mid-route
        /// would abandon itself halfway.</summary>
        bool running;

        bool lastRunWasToSchool;

        /// <summary>Which clock day each window was last served on, so a window that stays open
        /// still yields exactly one run. Day alone would not do it - at 1s/hour the day rolls
        /// over mid-run - which is why `running` exists alongside it.</summary>
        int morningServedDay = int.MinValue;
        int afternoonServedDay = int.MinValue;

        public State CurrentState => state;

        public void Bind(CityConfig cityConfig, SchoolBusDirector owner, Vector3 kerbPoint)
        {
            config = cityConfig;
            director = owner;
            schoolKerb = kerbPoint;

            car = GetComponent<CarBehavior>();
            cruiseSpeed = car.maxspeed;
            car.routeCompleted += OnRouteCompleted;

            // Aimed at where it already stands, and deliberately NOT held still by zeroing
            // maxspeed here: CarBehavior.Start seeds currentMaxSpeed from maxspeed once and
            // afterwards only on lane transitions, so a bus that started at zero would never
            // move again however the field was restored. Start paths it from the school kerb
            // to the school kerb, that trajectory ends within a tile, and the completion lands
            // in the handler below as an ordinary arrival - which parks it, in the lane, still
            // registered.
            driveTarget = schoolKerb;
            state = State.Returning;
            car.hasScriptedDestination = true;
            car.scriptedDestination = schoolKerb;

            StartCoroutine(ScheduleRoutine());
        }

        void OnDestroy()
        {
            if (car != null)
                car.routeCompleted -= OnRouteCompleted;
        }

        // ------------------------------------------------------------------ the schedule

        /// <summary>
        /// Watches the city clock for the two windows. Only the START is gated - see `running`.
        /// A session that opens inside a window joins it immediately, the way the police fleet
        /// opens mid-shift rather than as a depot emptying.
        /// </summary>
        IEnumerator ScheduleRoutine()
        {
            var clock = FindAnyObjectByType<Ambient.CityClock>();

            while (true)
            {
                yield return SchedulePoll;

                if (running || !clock)
                    continue;

                if (morningServedDay != clock.Day &&
                    SchoolRun.InWindow(clock.Hour, config.schoolMorningHour,
                                       config.schoolRunWindowHours))
                {
                    morningServedDay = clock.Day;
                    yield return RunRoutine(toSchool: true);
                }
                else if (afternoonServedDay != clock.Day &&
                         SchoolRun.InWindow(clock.Hour, config.schoolAfternoonHour,
                                            config.schoolRunWindowHours))
                {
                    afternoonServedDay = clock.Day;
                    yield return RunRoutine(toSchool: false);
                }
            }
        }

        /// <summary>Start a run now regardless of the clock - the director's inspector
        /// affordance. Alternates direction so repeated presses show both halves.</summary>
        public void ForceRun()
        {
            if (!running)
                StartCoroutine(RunRoutine(toSchool: !lastRunWasToSchool));
        }

        // ------------------------------------------------------------------ the run

        IEnumerator RunRoutine(bool toSchool)
        {
            running = true;
            lastRunWasToSchool = toSchool;

            // Morning: the children are at their stops and the school fills up. Afternoon: they
            // are inside the school and board here, at its own kerb, before the bus works the
            // stops in reverse putting them down again.
            if (!toSchool)
            {
                pavementHint = director.School ? director.School.StandWorld : schoolKerb;
                yield return LoadAtSchool();
            }

            foreach (var index in SchoolRun.StopOrder(director.Stops.Count, toSchool))
            {
                var stop = director.Stops[index];

                state = State.Driving;
                yield return DriveTo(stop.Kerb);
                if (!arrived)
                    continue;

                pavementHint = stop.Queue;
                state = State.Halted;
                yield return toSchool ? Board(index) : Alight(index, stop.Queue);
            }

            state = State.Returning;
            yield return DriveTo(schoolKerb);

            if (toSchool && arrived)
            {
                pavementHint = director.School ? director.School.StandWorld : schoolKerb;
                state = State.Unloading;
                yield return UnloadAtSchool();
            }

            state = State.Waiting;
            running = false;
        }

        /// <summary>
        /// Aims at a point, lets CarBehavior drive there, and brakes into it. Returns with
        /// <see cref="arrived"/> telling the caller whether this is actually the place - and,
        /// when it is, with the bus already halted by the route-completion handler.
        /// </summary>
        IEnumerator DriveTo(Vector3 target)
        {
            arrived = false;
            aimAttempts = 0;
            driveTarget = target;

            car.stopHere = false;
            car.maxspeed = cruiseSpeed;
            car.hasScriptedDestination = true;
            car.scriptedDestination = target;
            car.SetNewPath();

            while (!arrived && aimAttempts < AimRetries)
            {
                // Braking is a config change, not a transform change: desiredSpeed is recomputed
                // every frame from maxspeed (CarBehavior.cs:590), so lowering it decelerates the
                // bus through the pack's own model instead of fighting it for the transform.
                var remaining = Flat(target - transform.position).magnitude;
                car.maxspeed = remaining < BrakingDistance
                    ? Mathf.Lerp(ApproachSpeed, cruiseSpeed, remaining / BrakingDistance)
                    : cruiseSpeed;

                yield return null;
            }

            if (!arrived)
            {
                // Unreachable from here. Hand the bus back to ordinary driving so the next leg
                // starts from a car that is moving rather than from one stuck mid-manoeuvre.
                car.maxspeed = cruiseSpeed;
                car.hasScriptedDestination = false;
            }
        }

        /// <summary>
        /// The one window in which a halt can be claimed - see the class comment. Everything
        /// here has to happen inside this call, not a frame later.
        /// </summary>
        void OnRouteCompleted()
        {
            // The spent trajectory re-announces completion every frame, so a halted bus would
            // otherwise re-run all of this at 60Hz.
            if (arrived || state == State.Waiting || state == State.Halted)
                return;

            if (Flat(transform.position - driveTarget).sqrMagnitude > ArriveRange * ArriveRange)
            {
                // A re-path put the route's end somewhere else. Keep the override set and let
                // the next path aim again; give up after a few, so one unreachable stop cannot
                // pin the whole run.
                aimAttempts++;
                return;
            }

            arrived = true;
            car.hasScriptedDestination = false;
            car.stopHere = true;
            car.maxspeed = 0f;

            // The one completion that belongs to nobody: the park-where-you-stand route Bind
            // aims at the school kerb before the first window ever opens.
            if (!running)
                state = State.Waiting;
        }

        // ------------------------------------------------------------------ the door

        /// <summary>
        /// Where a child stands to get on or off: alongside the bus at its front, on the
        /// pavement side. Derived from the bus's ACTUAL transform rather than from the planned
        /// stop, because the route ended within a tile of that stop and not on it.
        /// </summary>
        public Vector3 DoorWorld
        {
            get
            {
                var side = Vector3.Cross(Vector3.up, transform.forward);
                if (Vector3.Dot(side, Flat(pavementHint - transform.position)) < 0f)
                    side = -side;

                return transform.position + transform.forward * 2.5f + side * 2.2f;
            }
        }

        // ------------------------------------------------------------------ stops

        /// <summary>
        /// The bus stands while the children at this stop get on, and leaves when they are
        /// aboard or when the cap expires - whichever comes first. Anyone left behind is told
        /// to walk, which is a visible outcome rather than a stuck one.
        /// </summary>
        IEnumerator Board(int stop)
        {
            director.ChildrenAt(stop, waiting);

            var order = 0;
            foreach (var child in waiting)
                if (child && child.CanBoardAtStop)
                    child.BeginBoarding(this, order++);

            yield return WaitFor(waiting, config.schoolBusDwellSeconds, c => c.Boarding);

            foreach (var child in waiting)
                if (child && child.Boarding)
                    child.GiveUpAndWalk();
        }

        /// <summary>The afternoon half: everyone whose home stop this is gets off here.</summary>
        IEnumerator Alight(int stop, Vector3 queue)
        {
            director.ChildrenAt(stop, waiting);

            var order = 0;
            foreach (var child in waiting)
                if (child && child.Riding)
                    child.BeginAlighting(this, order++, queue);

            yield return WaitFor(waiting, config.schoolBusDwellSeconds, c => c.Alighting);
        }

        // ------------------------------------------------------------------ the school

        /// <summary>
        /// At the school in the morning: everyone aboard gets off and goes in through the door.
        /// Off the road, so there is no lane being held and the fan-out can take its time.
        /// </summary>
        IEnumerator UnloadAtSchool()
        {
            var roster = director.Roster;
            var riding = new List<SchoolChildAgent>();

            foreach (var child in roster)
                if (child && child.Riding)
                    riding.Add(child);

            for (var i = 0; i < riding.Count; i++)
                riding[i].BeginSchoolArrival(this, i, riding.Count);

            yield return WaitFor(riding, SchoolLegTimeout, c => c.Alighting);
        }

        /// <summary>The afternoon start: the school empties into the bus at its own kerb.</summary>
        IEnumerator LoadAtSchool()
        {
            var roster = director.Roster;
            var order = 0;

            foreach (var child in roster)
                if (child && child.CanBoardAtSchool)
                    child.BeginBoarding(this, order++);

            yield return WaitFor(roster, SchoolLegTimeout, c => c.Boarding);

            foreach (var child in roster)
                if (child && child.Boarding)
                    child.GiveUpAndWalk();
        }

        /// <summary>Polls until nobody is still busy, or until the deadline. A poll rather than
        /// a countdown of callbacks for BankVisitorDirector's reason: a child can be destroyed,
        /// can give up, or can never have started, and one question covers all three.</summary>
        static IEnumerator WaitFor(
            IReadOnlyList<SchoolChildAgent> children,
            float seconds,
            System.Func<SchoolChildAgent, bool> busy)
        {
            var deadline = Time.time + seconds;
            while (Time.time < deadline)
            {
                var pending = false;
                for (var i = 0; i < children.Count; i++)
                    if (children[i] && busy(children[i]))
                    {
                        pending = true;
                        break;
                    }

                if (!pending)
                    yield break;

                yield return BoardPoll;
            }
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
