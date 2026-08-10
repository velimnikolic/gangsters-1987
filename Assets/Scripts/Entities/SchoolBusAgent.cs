using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.City;
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
    /// It is PolicePatrolAgent's machinery with a different errand. It HOMES like a patrol car -
    /// Resting in a berth in the school yard, Undocking onto the kerb to start a run, Docking
    /// back into the berth at the end - and it HALTS unlike one: a bus stop is a halt in a live
    /// lane, because that is what a bus stop is. The four notes below are all about the halt.
    ///
    /// The yard is new. The school used to have no landmarkCars, so PlaceLandmark stood it flush
    /// against the street wall with nothing in front of it, and the bus had nowhere to be
    /// between runs but the kerb it stopped at. It now has a bay of its own - see
    /// ParkingLayout.BusStallLength for why that bay lies along the street rather than nose-in.
    /// A school whose recessed placement failed still has none (SchoolMarker.HasBusStall), and
    /// this class still handles that: no berth means no dock and no undock, and the bus rests at
    /// the kerb exactly as it used to.
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
    /// CarBehavior stays ENABLED through everything on the road, for PolicePatrolAgent's reason:
    /// disabling it unregisters the bus from TrafficRegistry, and the one thing a bus stopped in
    /// a lane must be is something traffic can see and brake for. The one exception is the same
    /// one the patrol fleet has: a bus that STARTS the session in its berth keeps CarBehavior
    /// disabled until its first undock, because Start() calls SetNewPath and would teleport the
    /// parked bus onto a lane. The first undock enables it at the kerb, where it already stands
    /// facing down the lane.
    ///
    /// No per-frame Update: one coroutine drives the run, and CarBehavior does the driving -
    /// except through the berth manoeuvres, which are hand-animated along a PoliceDocking curve.
    /// </summary>
    public sealed class SchoolBusAgent : MonoBehaviour, UI.IOverlaySubject
    {
        /// <summary>
        /// Resting and Docking/Undocking are the yard; Driving/Halted/Returning are the road;
        /// Loading and Unloading are the two ends of a run, in the yard, with the doors open.
        /// Public because the overlay HUD colours the marker and words the popup off it.
        /// </summary>
        public enum State
        {
            Resting, Loading, Undocking, Driving, Halted, Returning, Docking, Unloading,
        }

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

        /// <summary>The gate-spawn footprint test, reused for pulling out of the berth - the
        /// patrol car's numbers, and a bus is longer than a squad car but the kerb point it
        /// pulls onto is the same one metre of road.</summary>
        const float KerbHalfLength = 6f;
        const float KerbHalfWidth = 1.5f;

        static readonly WaitForSeconds UndockPoll = new WaitForSeconds(1f);

        CityConfig config;
        SchoolBusDirector director;
        Vector3 schoolKerb;
        Vector3 schoolKerbDir;
        CarBehavior car;

        readonly List<SchoolChildAgent> waiting = new();

        State state;
        float cruiseSpeed;
        Vector3 driveTarget;
        bool arrived;
        int aimAttempts;

        /// <summary>
        /// Which way round the bus stands in its berth. The bay runs parallel to the street and
        /// the geometry cannot choose between its two directions - the bus has to face the way
        /// the traffic beside it runs, and that is only known once a road waypoint has been
        /// found. See SchoolMarker.BusStallRotation.
        /// </summary>
        bool berthFlip;

        /// <summary>Standing in the berth right now. False for a school with no berth at all,
        /// and false while the bus is out on a run.</summary>
        bool docked;

        /// <summary>True for a bus that began the session in its berth - see the class comment.
        /// Its CarBehavior is held disabled until the first undock.</summary>
        bool carNeverEnabled;

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

        /// <summary>Which stop of the run the bus is working on, 1-based, for the popup. Zero
        /// when it is not on the road.</summary>
        public int CurrentStop { get; private set; }

        /// <summary>How many stops this run has. The popup says "stop 2 of 3".</summary>
        public int StopCount => director ? director.Stops.Count : 0;

        /// <summary>True on the morning run (collecting), false on the afternoon one.</summary>
        public bool RunningToSchool => lastRunWasToSchool;

        public void Bind(
            CityConfig cityConfig,
            SchoolBusDirector owner,
            Vector3 kerbPoint,
            Vector3 kerbDirection,
            bool startInBerth,
            bool flip)
        {
            config = cityConfig;
            director = owner;
            schoolKerb = kerbPoint;
            schoolKerbDir = kerbDirection;
            berthFlip = flip;

            car = GetComponent<CarBehavior>();
            cruiseSpeed = car.maxspeed;
            car.routeCompleted += OnRouteCompleted;

            if (startInBerth)
            {
                // The director already stood it in the berth with CarBehavior disabled. Nothing
                // to aim and nothing to stop: the bus is off the lane graph entirely until its
                // first undock. Enabling CarBehavior here would run Start -> SetNewPath and
                // teleport it onto a lane, which is the patrol fleet's trap verbatim.
                docked = true;
                carNeverEnabled = true;
                state = State.Resting;
            }
            else
            {
                // No berth on this seed - the school stood flush. Aimed at where it already
                // stands, and deliberately NOT held still by zeroing maxspeed here: CarBehavior
                // .Start seeds currentMaxSpeed from maxspeed once and afterwards only on lane
                // transitions, so a bus that started at zero would never move again however the
                // field was restored. Start paths it from the school kerb to the school kerb,
                // that trajectory ends within a tile, and the completion lands in the handler
                // below as an ordinary arrival - which parks it, in the lane, still registered.
                driveTarget = schoolKerb;
                state = State.Returning;
                car.hasScriptedDestination = true;
                car.scriptedDestination = schoolKerb;
            }

            UI.OverlayRegistry.Register(this);
            StartCoroutine(ScheduleRoutine());
        }

        void OnDestroy()
        {
            UI.OverlayRegistry.Unregister(this);
            if (car != null)
                car.routeCompleted -= OnRouteCompleted;
        }

        // --------------------------------------------------------------- the overlay
        // Explicit implementation - the HUD's plumbing, not the bus's own API.

        /// <summary>The bus is 4.29m tall against a car's 1.6, so its marker floats higher than
        /// anything else in the city.</summary>
        const float MarkerHeight = 5f;

        Transform UI.IOverlaySubject.OverlayAnchor => transform;
        float UI.IOverlaySubject.OverlayHeight => MarkerHeight;
        bool UI.IOverlaySubject.OverlayHidden => false;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Diamond;
        Color UI.IOverlaySubject.OverlayColor => UI.SchoolIntention.BusColor(state);
        string UI.IOverlaySubject.OverlayTitle => UI.SchoolIntention.BusTitle();
        string UI.IOverlaySubject.OverlayLine =>
            UI.SchoolIntention.BusIntention(state, CurrentStop, StopCount, lastRunWasToSchool);
        long UI.IOverlaySubject.OverlayKey =>
            ((long)state << 32) | (uint)(CurrentStop * 2 + (lastRunWasToSchool ? 1 : 0));

        // ---------------------------------------------------------------------------------

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
            // are inside the school and board here, IN THE YARD, before the bus works the stops
            // in reverse putting them down again. Boarding before the undock rather than after
            // it is the whole point of having a yard: a class filing onto a bus standing in a
            // live lane was the old shape, and it held up every car behind for the dwell.
            if (!toSchool)
            {
                pavementHint = SchoolStand;
                state = State.Loading;
                yield return LoadAtSchool();
            }

            yield return Undock();

            var stops = director.Stops.Count;
            var served = 0;

            foreach (var index in SchoolRun.StopOrder(stops, toSchool))
            {
                var stop = director.Stops[index];

                CurrentStop = ++served;
                state = State.Driving;
                yield return DriveTo(stop.Kerb);
                if (!arrived)
                    continue;

                pavementHint = stop.Queue;
                state = State.Halted;
                yield return toSchool ? Board(index) : Alight(index, stop.Queue);
            }

            CurrentStop = 0;
            state = State.Returning;
            yield return DriveTo(schoolKerb);

            // Into the berth BEFORE the doors open, so the children step out onto the school's
            // own forecourt rather than into the road they were dropped in before.
            if (arrived)
                yield return Dock();

            if (toSchool)
            {
                pavementHint = SchoolStand;
                state = State.Unloading;
                yield return UnloadAtSchool();
            }

            state = State.Resting;
            running = false;
        }

        Vector3 SchoolStand => director.School ? director.School.StandWorld : schoolKerb;

        // ------------------------------------------------------------------ the berth

        /// <summary>
        /// Out of the berth and onto the kerb, hand-animated along the same curve the patrol
        /// fleet uses - with a longer mouth offset, because a 9.77m bus swung through the car
        /// figure pivots inside its own body (see PoliceDocking.BusMouthOffset).
        ///
        /// A no-op on a seed whose school has no berth: there the bus is already standing on the
        /// road and the run simply starts.
        /// </summary>
        IEnumerator Undock()
        {
            if (!docked)
                yield break;

            state = State.Undocking;

            var school = director.School;

            // The same footprint test a gate spawn applies, for the patrol car's reason: the
            // kerb is the road the bus is about to occupy, and rejecting a busy moment costs
            // nothing but the next poll.
            while (!TrafficRegistry.IsClear(
                       schoolKerb, schoolKerbDir, KerbHalfLength, KerbHalfWidth))
                yield return UndockPoll;

            var berthRot = school.BusStallRotation(berthFlip);
            var curve = PoliceDocking.Undock(
                school.BusStallWorld, school.BusStallOut(berthFlip), schoolKerb,
                PoliceDocking.BusMouthOffset);

            yield return Drive(curve, berthRot,
                               Quaternion.LookRotation(schoolKerbDir, Vector3.up));

            docked = false;

            if (carNeverEnabled)
            {
                // First enable runs CarBehavior.Start -> SetNewPath from right here at the kerb,
                // which is the whole reason the component was held off until now.
                carNeverEnabled = false;
                car.enabled = true;
            }
            else
            {
                car.stopHere = false;
                car.maxspeed = cruiseSpeed;
                car.hasScriptedDestination = false;
                car.SetNewPath();
            }
        }

        /// <summary>Off the kerb and into the berth. The bus arrives already halted - DriveTo
        /// returns with the route-completion handler having set stopHere - so the transform is
        /// this component's for the length of the curve and CarBehavior stays out of the way.
        /// It stays ENABLED throughout, so the bus crossing the pavement band is still something
        /// TrafficRegistry can see.</summary>
        IEnumerator Dock()
        {
            var school = director.School;
            if (!school || !school.HasBusStall)
                yield break;

            state = State.Docking;

            var berthRot = school.BusStallRotation(berthFlip);
            var curve = PoliceDocking.Dock(
                transform.position, school.BusStallWorld, school.BusStallOut(berthFlip),
                PoliceDocking.BusMouthOffset);

            yield return Drive(curve, transform.rotation, berthRot);
            docked = true;
        }

        /// <summary>The hand-animated leg: position along the curve at PoliceDocking.Speed,
        /// rotation slerped across it. TrafficRegistry needs no telling - it reads the transform
        /// live. Copied from PolicePatrolAgent rather than shared, as the rest of this class
        /// is.</summary>
        IEnumerator Drive(PoliceDocking.Curve curve, Quaternion from, Quaternion to)
        {
            var t = 0f;
            while (t < 1f)
            {
                t = PoliceDocking.Advance(curve, t, PoliceDocking.Speed * Time.deltaTime);
                transform.SetPositionAndRotation(
                    PoliceDocking.Point(curve, t),
                    Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t)));
                yield return null;
            }
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
            // otherwise re-run all of this at 60Hz. Tested as "am I driving" rather than as a
            // list of states to skip: the yard states are hand-animated, and a completion
            // arriving mid-manoeuvre must not be able to claim a halt on the transform this
            // component is writing every frame.
            if (arrived || (state != State.Driving && state != State.Returning))
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
            // aims at the school kerb before the first window ever opens, on a seed with no
            // berth to stand in.
            if (!running)
                state = State.Resting;
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

        /// <summary>The afternoon start: the school empties into the bus standing in its own
        /// yard, before it has pulled out.</summary>
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
