using System.Collections;
using UnityEngine;
using PolyPerfect.City;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// A response car: drives the road graph to the incident on CarBehavior's own scripted-
    /// destination patch - exactly the hooks PolicePatrolAgent rides, minus the docking
    /// choreography - parks at the kerb with stopHere (which keeps it registered in
    /// TrafficRegistry, so traffic still brakes for it), and tells the director to deploy
    /// its officers. Released, it simply rejoins traffic and despawns once its linger runs
    /// out, which in practice means off-screen.
    /// </summary>
    public sealed class PoliceResponseCar : MonoBehaviour, UI.IOverlaySubject
    {
        public enum State { Dormant, Driving, Arrived, Leaving }

        /// <summary>Re-aims tolerated before the car parks wherever its route ended.</summary>
        const int RetryLimit = 3;

        CarBehavior car;
        PoliceResponseDirector director;
        Vector3 incident;
        State state;
        int retries;

        public State CurrentState => state;

        public int UnitNumber { get; set; }

        PoliceConfig Police => GameplayRuntime.Police;

        public void Configure(PoliceResponseDirector owner)
        {
            director = owner;
            car = GetComponent<CarBehavior>();
            if (car)
                car.routeCompleted += OnRouteCompleted;
        }

        void OnEnable() => UI.OverlayRegistry.Register(this);

        void OnDisable() => UI.OverlayRegistry.Unregister(this);

        void OnDestroy()
        {
            if (car)
                car.routeCompleted -= OnRouteCompleted;
        }

        /// <summary>Send the car to the incident. The A* resolves the exact kerb.</summary>
        public void Dispatch(Vector3 incidentPosition)
        {
            if (!car)
                return;

            incident = incidentPosition;
            state = State.Driving;
            retries = 0;

            car.stopHere = false;
            car.hasScriptedDestination = true;
            car.scriptedDestination = incident;

            // A pooled car re-emerging mid-session must path itself; a fresh one is about
            // to run Start -> SetNewPath, which reads the scripted destination on its own.
            if (didFirstDispatch)
                car.SetNewPath();
            didFirstDispatch = true;
        }

        bool didFirstDispatch;

        void OnRouteCompleted()
        {
            if (state != State.Driving)
                return;

            var config = Police;
            var arriveRange = config ? config.carArriveRange : 25f;

            var flat = transform.position - incident;
            flat.y = 0f;

            if (flat.sqrMagnitude <= arriveRange * arriveRange || ++retries > RetryLimit)
            {
                // Parked. stopHere inside the routeCompleted window is the sanctioned stop -
                // CarBehavior halts without choosing another path (the patrol car's trick).
                car.hasScriptedDestination = false;
                car.stopHere = true;
                state = State.Arrived;
                if (director)
                    director.OnCarArrived(this);
                return;
            }

            // The route ended short of the incident - aim again; the next path gets closer.
            car.scriptedDestination = incident;
        }

        /// <summary>Back into traffic, gone after the linger - off-screen by then.</summary>
        public void Release()
        {
            if (state == State.Leaving || state == State.Dormant)
                return;

            state = State.Leaving;
            if (car)
            {
                car.stopHere = false;
                car.hasScriptedDestination = false;
                car.SetNewPath();
            }

            StartCoroutine(DespawnAfterLinger());
        }

        IEnumerator DespawnAfterLinger()
        {
            var config = Police;
            yield return new WaitForSeconds((config ? config.standDownSeconds : 8f) * 3f);

            state = State.Dormant;
            if (director)
                director.ReleaseCar(this);
        }

        // ------------------------------------------------------------------ the overlay

        Transform UI.IOverlaySubject.OverlayAnchor => transform;
        float UI.IOverlaySubject.OverlayHeight => 2.6f;
        bool UI.IOverlaySubject.OverlayHidden => state == State.Dormant;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Diamond;

        Color UI.IOverlaySubject.OverlayColor => state switch
        {
            State.Driving => new Color(0.25f, 0.5f, 0.95f),
            State.Arrived => new Color(0.95f, 0.3f, 0.2f),
            _ => new Color(0.5f, 0.55f, 0.6f),
        };

        string UI.IOverlaySubject.OverlayTitle => $"Response Car {UnitNumber}";

        string UI.IOverlaySubject.OverlayLine => state switch
        {
            State.Driving => "Racing to the scene",
            State.Arrived => "On scene",
            State.Leaving => "Returning to patrol",
            _ => "Idle",
        };

        long UI.IOverlaySubject.OverlayKey => (long)state;
    }
}
