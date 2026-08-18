using System.Collections;
using UnityEngine;
using LivingCity.City;
using LivingCity.Data;

namespace LivingCity.Entities
{
    /// <summary>
    /// The brain of one patrol car, for the whole of its life. The fleet is PERSISTENT: the
    /// same four GameObjects cycle Resting -> Undocking -> Patrolling -> Returning -> Docking
    /// forever, and none of the traffic system's churn applies - no TrafficAgent, no exit
    /// gates, no VehicleSpawner bookkeeping. PoliceDirector spawns the car and calls Bind;
    /// everything after that is this component riding CarBehavior's own hooks
    /// (scriptedDestination / routeCompleted / stopHere - the pair built for TrafficAgent).
    ///
    /// CarBehavior stays ENABLED throughout, held still with stopHere: its Update early-outs
    /// but the car stays registered in TrafficRegistry, whose probes read positions live off
    /// the transform - so a car crossing the pavement band into its stall is still an
    /// obstacle traffic brakes for, and the hand-animated moves below need no re-bucketing.
    /// Disabling it would unregister (see CarBehavior.OnDisable) and make the docking car a
    /// ghost. The one exception is a car that STARTS a session parked: its CarBehavior is
    /// held disabled by the director so that Start()'s SetNewPath cannot teleport it out of
    /// the stall onto a lane, and the first undock enables it - Start then paths from the
    /// kerb, where the car already stands facing down the lane, and the AlreadyDriving match
    /// usually swallows the snap.
    ///
    /// No per-frame Update: a coroutine runs the parked half of the cycle, and the driving
    /// half is entirely CarBehavior's - this component only hears routeCompleted.
    /// </summary>
    public sealed class PolicePatrolAgent : MonoBehaviour, UI.IOverlaySubject
    {
        /// <summary>Public because the overlay HUD colours its indicator and words its popup
        /// off it. The transitions stay this component's alone.</summary>
        public enum State { Resting, Undocking, Patrolling, Returning, Docking }

        /// <summary>Metres above the car's origin to float its marker - the roof sits ~1.6m
        /// up and this floats a little clear of it.</summary>
        const float MarkerHeight = 2.6f;

        /// <summary>
        /// How near the kerb point a completed route must end before the car docks. A route's
        /// A* destination is resolved to the nearest TILE, so a completed return route ends
        /// within a tile length of the kerb - 20m accepts that and rejects a completion that
        /// a re-path landed somewhere else entirely.
        /// </summary>
        const float DockRange = 20f;

        /// <summary>The gate-spawn footprint test, reused for pulling out of the stall.</summary>
        const float KerbHalfLength = 5f;
        const float KerbHalfWidth = 1.5f;

        static readonly WaitForSeconds UndockPoll = new WaitForSeconds(1f);

        CityConfig config;
        PoliceStation station;
        Vector3 kerbPos;
        Vector3 kerbDir;
        System.Random rng;
        CarBehavior car;

        State state;
        int stall = -1;
        int routesRemaining;

        /// <summary>True for a car that began the session parked - see the class comment.</summary>
        bool carNeverEnabled;

        public State CurrentState => state;
        public int RoutesRemaining => routesRemaining;

        /// <summary>1-based, set by the director - "Patrol Car 2" on the popup.</summary>
        public int UnitNumber { get; private set; }

        public void Bind(
            CityConfig cityConfig,
            PoliceStation home,
            Vector3 kerbPoint,
            Vector3 kerbDirection,
            int seed,
            int startStall,
            int unitNumber)
        {
            config = cityConfig;
            station = home;
            kerbPos = kerbPoint;
            kerbDir = kerbDirection;
            rng = new System.Random(seed);
            UnitNumber = unitNumber;
            UI.OverlayRegistry.Register(this);

            car = GetComponent<CarBehavior>();
            car.routeCompleted += OnRouteCompleted;

            if (startStall >= 0)
            {
                stall = startStall;
                carNeverEnabled = true;
                // A mid-shift opening: the rest timer starts partly spent, drawn per car, so
                // the parked fleet does not pull out in convoy on the session's first minute.
                StartCoroutine(RestRoutine(partlySpent: true));
            }
            else
            {
                routesRemaining = DrawRoutes();
                state = State.Patrolling;
            }
        }

        // --------------------------------------------------------------- the overlay
        // Explicit implementation: this is the HUD's plumbing, not the car's own API, and
        // keeping it off the public surface means nothing else can start depending on it.

        Transform UI.IOverlaySubject.OverlayAnchor => transform;
        float UI.IOverlaySubject.OverlayHeight => MarkerHeight;
        bool UI.IOverlaySubject.OverlayHidden => false;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Diamond;
        Color UI.IOverlaySubject.OverlayColor => UI.PoliceIntention.CarColor(state);
        string UI.IOverlaySubject.OverlayTitle => UI.PoliceIntention.CarTitle(UnitNumber);
        string UI.IOverlaySubject.OverlayLine =>
            UI.PoliceIntention.CarIntention(state, routesRemaining);
        long UI.IOverlaySubject.OverlayKey => ((long)state << 32) | (uint)routesRemaining;

        // ---------------------------------------------------------------------------------

        void OnDestroy()
        {
            UI.OverlayRegistry.Unregister(this);
            if (car != null)
                car.routeCompleted -= OnRouteCompleted;
            if (station && stall >= 0)
                station.ReleaseStall(stall);
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
                    AimAtKerb();
                    return;

                case State.Returning:
                    var flat = transform.position - kerbPos;
                    flat.y = 0f;
                    if (flat.sqrMagnitude > DockRange * DockRange)
                    {
                        // A re-path put the route's end somewhere other than the kerb - keep
                        // the override set and let the next path aim home again.
                        AimAtKerb();
                        return;
                    }

                    if (station && station.TryClaimStall(out stall))
                    {
                        // stopHere lands inside the routeCompleted window, which is exactly
                        // what it is for: CarBehavior stops without choosing another path,
                        // and the transform is this component's until the next undock.
                        car.hasScriptedDestination = false;
                        car.stopHere = true;
                        state = State.Docking;
                        StartCoroutine(DockRoutine());
                        return;
                    }

                    // Every stall taken - possible whenever the fleet outnumbers the bays.
                    // One more ordinary wander route and try again; the misses stagger the
                    // fleet all by themselves.
                    car.hasScriptedDestination = false;
                    return;
            }
        }

        void AimAtKerb()
        {
            car.hasScriptedDestination = true;
            car.scriptedDestination = kerbPos;
        }

        IEnumerator DockRoutine()
        {
            var targetRot = station.StallRotation(stall);
            var curve = PoliceDocking.Dock(
                transform.position, station.StallWorld(stall), targetRot * Vector3.forward);

            yield return Drive(curve, transform.rotation, targetRot);
            yield return RestRoutine(partlySpent: false);
        }

        IEnumerator RestRoutine(bool partlySpent)
        {
            state = State.Resting;

            var rest = Range(config.policeCarRestRange);
            yield return new WaitForSeconds(partlySpent ? rest * (float)rng.NextDouble() : rest);

            state = State.Undocking;

            // The same footprint test a gate spawn applies, for the same reason: the kerb is
            // the one metre of road the car is about to occupy, and rejecting a busy moment
            // costs nothing but the next poll.
            while (!TrafficRegistry.IsClear(kerbPos, kerbDir, KerbHalfLength, KerbHalfWidth))
                yield return UndockPoll;

            var stallRot = station.StallRotation(stall);
            var curve = PoliceDocking.Undock(
                station.StallWorld(stall), stallRot * Vector3.forward, kerbPos, kerbDir);

            yield return DriveOut(curve, Quaternion.LookRotation(kerbDir, Vector3.up));

            station.ReleaseStall(stall);
            stall = -1;
            routesRemaining = DrawRoutes();
            state = State.Patrolling;

            if (carNeverEnabled)
            {
                // First enable runs CarBehavior.Start -> SetNewPath from right here at the
                // kerb, which is the whole reason the component was held off until now.
                carNeverEnabled = false;
                car.enabled = true;
            }
            else
            {
                car.stopHere = false;
                car.hasScriptedDestination = false;
                car.SetNewPath();
            }
        }

        /// <summary>The hand-animated leg INTO the bay: position along the curve at
        /// PoliceDocking.Speed, rotation slerped across it. Slerped and not steered on purpose -
        /// the car backs in, so it ends nose-out while travelling nose-first no longer applies.
        /// TrafficRegistry needs no telling - it reads the transform live.</summary>
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

        /// <summary>The hand-animated leg OUT of the bay, which the car drives forwards: the
        /// heading is the curve's own tangent, so the car points where it is going instead of
        /// gliding sideways out of the forecourt while its yaw slerps onto the lane. The
        /// outbound curve is built to end along kerbDir, so the last tangent IS
        /// <paramref name="onLane"/> - which stands in only where a degenerate curve leaves no
        /// direction to read.</summary>
        IEnumerator DriveOut(PoliceDocking.Curve curve, Quaternion onLane)
        {
            var t = 0f;
            while (t < 1f)
            {
                t = PoliceDocking.Advance(curve, t, PoliceDocking.Speed * Time.deltaTime);
                transform.SetPositionAndRotation(
                    PoliceDocking.Point(curve, t),
                    PoliceDocking.Heading(curve, t, onLane));
                yield return null;
            }
        }

        int DrawRoutes()
        {
            var routes = config.policeCarPatrolRoutes;
            return Mathf.Max(1, rng.Next(Mathf.Min(routes.x, routes.y),
                                         Mathf.Max(routes.x, routes.y) + 1));
        }

        float Range(Vector2 range) =>
            range.x + (float)rng.NextDouble() * Mathf.Max(0f, range.y - range.x);
    }
}
