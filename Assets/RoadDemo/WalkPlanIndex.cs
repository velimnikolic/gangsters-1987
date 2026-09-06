using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// THE PLAN INDEX. Every point a walker probes used to be put to every registered
    /// pavement plan - 216 of them in the full city, each answering "not mine" after a
    /// bounds check, 5.5 of the 6.8 microseconds a probe cost (measured 2026-09-06),
    /// and a steer probes a few dozen points a frame. The plans are laid out by area,
    /// so a coarse grid of which plans touch which 32 m cell answers a probe with the
    /// one or two that can matter. WalkObstacles owns the plans and their Version; the
    /// index is rebuilt whenever that Version moves, which every registration and every
    /// box a plan takes or drops already does.
    /// </summary>
    static class WalkPlanIndex
    {
        const float PlanCell = 32f;
        static readonly Dictionary<long, List<SidewalkPlan>> PlanCells =
            new Dictionary<long, List<SidewalkPlan>>();
        static readonly List<SidewalkPlan> PlansScratch = new List<SidewalkPlan>();
        static int _planIndexVersion = -1;
        static int _planStamp;

        static long PlanKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        static void Rebuild(List<SidewalkPlan> plans, int version)
        {
            PlanCells.Clear();
            for (int i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                if (plan == null || !plan.TryGetExtent(out var min, out var max)) continue;
                int x0 = Mathf.FloorToInt(min.x / PlanCell), x1 = Mathf.FloorToInt(max.x / PlanCell);
                int z0 = Mathf.FloorToInt(min.y / PlanCell), z1 = Mathf.FloorToInt(max.y / PlanCell);
                for (int cx = x0; cx <= x1; cx++)
                    for (int cz = z0; cz <= z1; cz++)
                    {
                        long key = PlanKey(cx, cz);
                        if (!PlanCells.TryGetValue(key, out var cell))
                            PlanCells.Add(key, cell = new List<SidewalkPlan>(2));
                        cell.Add(plan);
                    }
            }
            _planIndexVersion = version;
        }

        /// <summary>The registered plans whose ground touches the square from
        /// <paramref name="lo"/> to <paramref name="hi"/>, each once, in registration
        /// order within a cell. One shared list: read it before asking again. The
        /// square is widened by one of the plans' own 4 m cells so the answer is never
        /// narrower than a plan's own bounds test.</summary>
        public static List<SidewalkPlan> Near(List<SidewalkPlan> plans, int version, Vector2 lo, Vector2 hi)
        {
            if (_planIndexVersion != version) Rebuild(plans, version);
            PlansScratch.Clear();
            if (PlanCells.Count == 0) return PlansScratch;
            const float berth = 4f;
            int x0 = Mathf.FloorToInt((lo.x - berth) / PlanCell), x1 = Mathf.FloorToInt((hi.x + berth) / PlanCell);
            int z0 = Mathf.FloorToInt((lo.y - berth) / PlanCell), z1 = Mathf.FloorToInt((hi.y + berth) / PlanCell);
            int stamp = ++_planStamp;
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    if (!PlanCells.TryGetValue(PlanKey(cx, cz), out var cell)) continue;
                    for (int k = 0; k < cell.Count; k++)
                    {
                        var plan = cell[k];
                        if (plan.VisitStamp == stamp) continue;
                        plan.VisitStamp = stamp;
                        PlansScratch.Add(plan);
                    }
                }
            return PlansScratch;
        }

        public static List<SidewalkPlan> Near(List<SidewalkPlan> plans, int version, Vector2 at, float reach) =>
            Near(plans, version, new Vector2(at.x - reach, at.y - reach), new Vector2(at.x + reach, at.y + reach));

        /// <summary>A new Play: nothing is indexed until somebody asks.</summary>
        public static void Forget()
        {
            PlanCells.Clear();
            PlansScratch.Clear();
            _planIndexVersion = -1;
        }
    }
}
