using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Advances all road users together, including accelerated frames.</summary>
#if UNITY_5_3_OR_NEWER
    [DefaultExecutionOrder(-10000)]
#endif
    public sealed class RoadCarSimulation
#if UNITY_5_3_OR_NEWER
        : MonoBehaviour
#endif
    {
        const float MaxStep = 1f / 30f;
        static bool stepping;
        static float stepTime;
        public static float Now => stepping ? stepTime : Time.time;

        public static void Simulate(IReadOnlyList<RoadCar> cars, float elapsed) =>
            Simulate(cars, elapsed, null);

        static void Simulate(IReadOnlyList<RoadCar> cars, float elapsed,
            Dictionary<RoadCar, float> budgets)
        {
            if (elapsed <= 0f || cars.Count == 0) return;
            int steps = Mathf.Max(1, Mathf.CeilToInt(elapsed / MaxStep));
            float step = elapsed / steps;
            float end = Time.time;
            stepping = true;
            try
            {
                for (int i = 0; i < steps; i++)
                {
                    stepTime = end - elapsed + (i + 1) * step;
                    RoadSpace.Invalidate();
                    for (int c = 0; c < cars.Count; c++)
                    {
                        var car = cars[c];
                        if (car == null) continue;
                        float dt = budgets == null ? step : budgets[car] / steps;
                        car.TickStep(dt);
                    }
                }
            }
            finally { stepping = false; RoadSpace.Invalidate(); }
        }

#if UNITY_5_3_OR_NEWER
        static RoadCarSimulation instance;
        readonly List<RoadCar> pending = new List<RoadCar>();
        readonly Dictionary<RoadCar, float> budgets = new Dictionary<RoadCar, float>();
        float elapsed;

        internal static void Queue(RoadCar car, float dt)
        {
            if (instance == null)
                instance = new GameObject("Road simulation").AddComponent<RoadCarSimulation>();
            if (!instance.budgets.ContainsKey(car)) instance.pending.Add(car);
            instance.budgets[car] = dt;
            instance.elapsed = Mathf.Max(instance.elapsed, dt);
        }

        void LateUpdate()
        {
            try { Simulate(pending, elapsed, budgets); }
            finally
            {
                pending.Clear();
                budgets.Clear();
                elapsed = 0f;
            }
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
#endif
    }
}
