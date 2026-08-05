using UnityEngine;
using PolyPerfect.City;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Gives one car a life: a few errands around the city, then out through a gap in the map's
    /// outline, at which point it is taken away and replaced by one arriving through another.
    ///
    /// This is what stops traffic being a fixed cast of thirty cars that can only ever have got
    /// there by materialising. The package has no notion of a car leaving - CarBehavior picks a
    /// new random tile the instant it reaches one, forever - so the route budget is kept here
    /// and applied through the routeCompleted hook patched into CarBehavior.
    ///
    /// Added by VehicleSpawner in the window between Instantiate and the end of the frame, which
    /// is when the car's own Start() runs. That ordering is what makes it safe to set the first
    /// destination from here: by the time CarBehavior.SetNewPath() looks, everything is in place.
    /// </summary>
    [RequireComponent(typeof(CarBehavior))]
    public sealed class TrafficAgent : MonoBehaviour
    {
        CarBehavior car;
        MapEdgeGates gates;
        VehicleSpawner spawner;
        System.Random rng;

        int wanderRoutes;
        int routesServed;
        bool leaving;

        public void Bind(VehicleSpawner owner, MapEdgeGates edgeGates, System.Random random, int routes)
        {
            spawner = owner;
            gates = edgeGates;
            rng = random;
            wanderRoutes = Mathf.Max(0, routes);

            car = GetComponent<CarBehavior>();
            if (car)
                car.routeCompleted += OnRouteCompleted;
        }

        void OnRouteCompleted()
        {
            if (leaving)
            {
                // The car is standing on the outline of the map with its trip finished. Claim it
                // before CarBehavior can choose somewhere else to be.
                car.stopHere = true;
                spawner.Replace(gameObject);
                return;
            }

            routesServed++;
            if (routesServed < wanderRoutes)
                return;

            // No gate to aim at leaves the car wandering, which is the pre-existing behaviour and
            // harmless - it will be offered a gate again at the end of the next route.
            if (!gates || !gates.TryPickExit(transform.position, rng, out var exit))
                return;

            car.scriptedDestination = exit.Point;
            car.hasScriptedDestination = true;
            leaving = true;
        }

        void OnDestroy()
        {
            if (car)
                car.routeCompleted -= OnRouteCompleted;
        }
    }
}
