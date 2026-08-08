using System.Collections;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// One customer's trip to the bank: in off the map, into a forecourt bay, a while inside,
    /// and back out through the boundary for good. Unlike the patrol fleet this car is NOT
    /// persistent - the whole point is that a different vehicle stands there each time, so the
    /// car is destroyed on its way out and BankVisitorDirector sends another.
    ///
    /// Everything below is PolicePatrolAgent's machinery with different timers: the same
    /// CarBehavior hooks (scriptedDestination / routeCompleted / stopHere), the same
    /// PoliceDocking curve for the leg off the lane graph, the same kerb-clearance poll before
    /// pulling out. It is a second customer of that design rather than a second copy of it -
    /// the only thing genuinely new here is that the cycle ends instead of looping.
    ///
    /// CarBehavior stays ENABLED throughout, held still with stopHere, for the reason spelled
    /// out in PolicePatrolAgent: disabling it unregisters the car from TrafficRegistry, and a
    /// car crossing the pavement band into a bay has to stay something traffic brakes for.
    /// There is no start-parked case here, so none of the director's disable dance applies.
    /// </summary>
    public sealed class BankVisitorAgent : MonoBehaviour
    {
        public enum State { Arriving, Docking, Visiting, Leaving }

        /// <summary>
        /// How near the kerb a completed route must end before the car turns in. A route's A*
        /// destination resolves to the nearest TILE, so an arriving route ends within a tile
        /// length of the kerb - PolicePatrolAgent's 20m accepts that and rejects a completion a
        /// re-path landed somewhere else entirely.
        /// </summary>
        const float DockRange = 20f;

        /// <summary>The gate-spawn footprint test, reused for pulling out of the stall.</summary>
        const float KerbHalfLength = 5f;
        const float KerbHalfWidth = 1.5f;

        static readonly WaitForSeconds UndockPoll = new WaitForSeconds(1f);

        CityConfig config;
        BankForecourt forecourt;
        MapEdgeGates gates;
        BankVisitorDirector director;
        Vector3 kerbPos;
        Vector3 kerbDir;
        System.Random rng;
        CarBehavior car;

        State state;
        int stall = -1;

        public State CurrentState => state;

        public void Bind(
            CityConfig cityConfig,
            BankForecourt bank,
            MapEdgeGates edgeGates,
            BankVisitorDirector owner,
            Vector3 kerbPoint,
            Vector3 kerbDirection,
            int seed)
        {
            config = cityConfig;
            forecourt = bank;
            gates = edgeGates;
            director = owner;
            kerbPos = kerbPoint;
            kerbDir = kerbDirection;
            rng = new System.Random(seed);

            car = GetComponent<CarBehavior>();
            car.routeCompleted += OnRouteCompleted;

            state = State.Arriving;
            AimAt(kerbPos);
        }

        void OnDestroy()
        {
            if (car != null)
                car.routeCompleted -= OnRouteCompleted;
            if (forecourt && stall >= 0)
                forecourt.ReleaseStall(stall);
            if (director)
                director.Departed(this);
        }

        void OnRouteCompleted()
        {
            switch (state)
            {
                case State.Arriving:
                    var flat = transform.position - kerbPos;
                    flat.y = 0f;
                    if (flat.sqrMagnitude > DockRange * DockRange)
                    {
                        // A re-path put the route's end somewhere other than the kerb - keep
                        // aiming and let the next one bring the car home.
                        AimAt(kerbPos);
                        return;
                    }

                    if (forecourt && forecourt.TryClaimStall(out stall))
                    {
                        // stopHere lands inside the routeCompleted window, which is exactly
                        // what it is for: CarBehavior stops without choosing another path, and
                        // the transform is this component's until the car pulls out again.
                        car.hasScriptedDestination = false;
                        car.stopHere = true;
                        state = State.Docking;
                        StartCoroutine(VisitRoutine());
                        return;
                    }

                    // Forecourt full. One ordinary wander route and try again - the misses are
                    // what stagger arrivals without a queue to model.
                    car.hasScriptedDestination = false;
                    return;

                case State.Leaving:
                    // Standing on the outline of the map with the trip finished. Claim the car
                    // before CarBehavior can choose somewhere else to be, exactly as
                    // TrafficAgent does on its way out.
                    car.stopHere = true;
                    Destroy(gameObject);
                    return;
            }
        }

        void AimAt(Vector3 destination)
        {
            car.hasScriptedDestination = true;
            car.scriptedDestination = destination;
        }

        IEnumerator VisitRoutine()
        {
            var stallRot = forecourt.StallRotation(stall);

            var inbound = PoliceDocking.Dock(
                transform.position, forecourt.StallWorld(stall), stallRot * Vector3.forward);
            yield return Drive(inbound, transform.rotation, stallRot);

            state = State.Visiting;
            yield return new WaitForSeconds(Range(config.bankVisitStayRange));

            state = State.Leaving;

            // The same footprint test a gate spawn applies, for the same reason: the kerb is
            // the one metre of road the car is about to occupy, and rejecting a busy moment
            // costs nothing but the next poll.
            while (!TrafficRegistry.IsClear(kerbPos, kerbDir, KerbHalfLength, KerbHalfWidth))
                yield return UndockPoll;

            var outbound = PoliceDocking.Undock(
                forecourt.StallWorld(stall), stallRot * Vector3.forward, kerbPos);
            yield return Drive(outbound, stallRot, Quaternion.LookRotation(kerbDir, Vector3.up));

            forecourt.ReleaseStall(stall);
            stall = -1;

            car.stopHere = false;

            // Out through a gap in the map's outline, and then destroyed there - the same exit
            // TrafficAgent gives an ordinary car, for the same reason: a car that pops out of
            // existence mid-street is the single most artificial thing in the scene.
            //
            // A scene with no gates has nowhere to send it, so the visit simply repeats: the
            // car goes back round to the bank instead of vanishing. That is the same fallback
            // VehicleSpawner takes when a hand-assembled scene has no outline to leave by.
            if (gates && gates.TryPickExit(transform.position, rng, out var exit))
            {
                AimAt(exit.Point);
            }
            else
            {
                state = State.Arriving;
                AimAt(kerbPos);
            }

            car.SetNewPath();
        }

        /// <summary>The hand-animated leg: position along the curve at PoliceDocking.Speed,
        /// rotation slerped across it. TrafficRegistry needs no telling - it reads the
        /// transform live.</summary>
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

        float Range(Vector2 range) =>
            range.x + (float)rng.NextDouble() * Mathf.Max(0f, range.y - range.x);
    }
}
