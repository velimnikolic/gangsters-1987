using System.Collections.Generic;
using PolyPerfect.City;

namespace LivingCity.Entities
{
    /// <summary>
    /// The re-path budget. HumanBehavior re-paths the instant a walker finishes a route, and
    /// with thousands of walkers those A* searches land in unsynchronised bursts - dozens in
    /// one frame, each with two Physics.OverlapBox calls, which reads as a hitch. Instead the
    /// walker stands down (isMoving = false, which is just a person pausing at the kerb) and
    /// queues here; PedestrianLodSystem drains a fixed number per frame.
    ///
    /// Inactive until the spawner arms it, so the pack's demo scenes - no spawner, no
    /// director - keep their original immediate re-path.
    /// </summary>
    public static class PedestrianRepathQueue
    {
        /// <summary>Armed by PedestrianSpawner for the population it owns.</summary>
        public static bool Active;

        /// <summary>Re-paths granted per rendered frame, handed down from CityConfig.</summary>
        public static int BudgetPerFrame = 24;

        static readonly Queue<HumanBehavior> Pending = new Queue<HumanBehavior>();

        public static int PendingCount => Pending.Count;

        /// <summary>
        /// One entry per arrival by construction: an enqueued walker has isMoving false, so
        /// its FixedUpdate cannot reach route completion again before Repath runs.
        /// </summary>
        public static void Enqueue(HumanBehavior walker)
        {
            if (walker)
                Pending.Enqueue(walker);
        }

        /// <summary>Called once per frame by whoever drives the pedestrian systems.</summary>
        public static void Drain()
        {
            for (var granted = 0; granted < BudgetPerFrame && Pending.Count > 0; granted++)
            {
                var walker = Pending.Dequeue();
                if (walker && walker.isActiveAndEnabled)
                    walker.Repath();
            }
        }
    }
}
