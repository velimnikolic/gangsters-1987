using System.Collections;
using UnityEngine;
using LivingCity.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// One driver's trip to a forecourt: in off the map, into a bay, a while inside, and back
    /// out through the boundary for good. Unlike the patrol fleet this car is NOT persistent -
    /// the whole point is that a different vehicle stands there each time, so the car is
    /// destroyed on its way out and its director sends another.
    ///
    /// It knows nothing about WHICH forecourt. It was BankVisitorAgent, and the only thing that
    /// made it the bank's was the type of one field; the school's parents want exactly this trip
    /// with a different building, a different dwell and a different word in the popup, so the
    /// field is a StallHost and the three differences arrive through Bind. The file was RENAMED
    /// rather than copied - the class is added with AddComponent at runtime and appears in no
    /// scene, so nothing was bound to its old name.
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
    public sealed class ForecourtVisitorAgent : MonoBehaviour, UI.IOverlaySubject
    {
        /// <summary>Public because the overlay HUD colours the marker and words the popup off
        /// it. The transitions stay this component's alone.</summary>
        public enum State { Arriving, Docking, Visiting, Leaving }

        /// <summary>Metres above the car's origin to float its marker - the patrol car's.</summary>
        const float MarkerHeight = 2.6f;

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

        StallHost forecourt;
        MapEdgeGates gates;
        ForecourtVisitorDirector director;
        Vector3 kerbPos;
        Vector3 kerbDir;
        Vector2 stayRange;
        UI.ForecourtErrand errand;
        System.Random rng;
        CarBehavior car;

        State state;
        int stall = -1;

        public State CurrentState => state;

        /// <summary>Which errand this driver is on - the one thing that makes the popup say
        /// "the bank" or "the school" rather than "a building".</summary>
        public UI.ForecourtErrand Errand => errand;

        public void Bind(
            StallHost host,
            MapEdgeGates edgeGates,
            ForecourtVisitorDirector owner,
            Vector3 kerbPoint,
            Vector3 kerbDirection,
            Vector2 stay,
            UI.ForecourtErrand onErrand,
            int seed)
        {
            forecourt = host;
            gates = edgeGates;
            director = owner;
            kerbPos = kerbPoint;
            kerbDir = kerbDirection;
            stayRange = stay;
            errand = onErrand;
            rng = new System.Random(seed);

            car = GetComponent<CarBehavior>();
            car.routeCompleted += OnRouteCompleted;

            state = State.Arriving;
            AimAt(kerbPos);
            UI.OverlayRegistry.Register(this);
        }

        // --------------------------------------------------------------- the overlay
        // Explicit implementation - the HUD's plumbing, not the visitor's own API.

        Transform UI.IOverlaySubject.OverlayAnchor => transform;
        float UI.IOverlaySubject.OverlayHeight => MarkerHeight;
        bool UI.IOverlaySubject.OverlayHidden => false;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Diamond;
        Color UI.IOverlaySubject.OverlayColor => UI.ForecourtIntention.VisitorColor(state);
        string UI.IOverlaySubject.OverlayTitle => UI.ForecourtIntention.VisitorTitle(errand);
        string UI.IOverlaySubject.OverlayLine =>
            UI.ForecourtIntention.VisitorIntention(state, errand);
        long UI.IOverlaySubject.OverlayKey => (long)state;

        // ---------------------------------------------------------------------------------

        void OnDestroy()
        {
            UI.OverlayRegistry.Unregister(this);
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
            yield return new WaitForSeconds(Range(stayRange));

            state = State.Leaving;

            // The same footprint test a gate spawn applies, for the same reason: the kerb is
            // the one metre of road the car is about to occupy, and rejecting a busy moment
            // costs nothing but the next poll.
            while (!TrafficRegistry.IsClear(kerbPos, kerbDir, KerbHalfLength, KerbHalfWidth))
                yield return UndockPoll;

            var outbound = PoliceDocking.Undock(
                forecourt.StallWorld(stall), stallRot * Vector3.forward, kerbPos, kerbDir);
            yield return DriveOut(outbound, Quaternion.LookRotation(kerbDir, Vector3.up));

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

        /// <summary>The hand-animated leg INTO the bay: position along the curve at
        /// PoliceDocking.Speed, rotation slerped across it - the car backs in, so its heading is
        /// not its direction of travel. TrafficRegistry needs no telling - it reads the
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

        /// <summary>The hand-animated leg OUT of the bay, which the car drives forwards: steered
        /// by the curve's own tangent, so it does not glide out of the forecourt sideways. See
        /// PolicePatrolAgent.DriveOut, which this is copied from as the rest of this pair is.
        /// </summary>
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

        float Range(Vector2 range) =>
            range.x + (float)rng.NextDouble() * Mathf.Max(0f, range.y - range.x);
    }
}
