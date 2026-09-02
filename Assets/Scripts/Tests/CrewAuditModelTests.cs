using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>
    /// Pure contracts behind CrewAudit's live formation checks and the streamed
    /// walking-obstacle classifier. No scene, walker or UnityEngine.Object is created,
    /// so these run while the editor is idle.
    /// </summary>
    public static class CrewAuditModelTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();
            AlignedStepsStayTogether(failures);
            DivergentStepIsMeasured(failures);
            StationaryJitterHasNoHeading(failures);
            GroundSpreadIgnoresHeight(failures);
            BreachNeedsItsWholeGrace(failures);
            ClearingABreachResetsTheClock(failures);
            ComposedCafeFurnitureIsPhysical(failures);
            ComposedVenueShellsArePhysical(failures);
            return failures;
        }

        /// <summary>The streamed residential collector sees prefab roots by these names,
        /// and uses the same classifier on a shared mesh when a composer renamed the root.
        /// Table dressing must not turn a mug or plate into a walking obstacle.</summary>
        static void ComposedCafeFurnitureIsPhysical(List<string> failures)
        {
            var furniture = new[]
            {
                "SM_Prop_Table_02",
                "SM_Prop_Chair_01",
            };
            for (int i = 0; i < furniture.Length; i++)
                if (!WalkObstacles.PhysicalPropName(furniture[i]))
                    failures.Add($"Walk props: streamed cafe furniture {furniture[i]} was not physical.");

            var dressing = new[]
            {
                "SM_Gen_Prop_Mug_01",
                "SM_Gen_Prop_Plate_01",
            };
            for (int i = 0; i < dressing.Length; i++)
                if (WalkObstacles.PhysicalPropName(dressing[i]))
                    failures.Add($"Walk props: table dressing {dressing[i]} became a physical obstacle.");
        }

        /// <summary>A CityKit restaurant shell is not an SM_Prop_. Its authored root
        /// collider must still enter a streamed view's walking plan. Harvested cafes use
        /// their structural proxy first, but their runtime `(cafe)` identity remains a
        /// conservative fallback. Neither rule broadens to courts and car yards.</summary>
        static void ComposedVenueShellsArePhysical(List<string> failures)
        {
            var venues = new[]
            {
                "building-diner (cafe)",
                "building-coffeeshop (cafe)",
                "building-burger-joint (cafe)",
                "building-cafe",
                "building-restaurant",
                "pizzapub (cafe)",
                "pizzapub2 (cafe)",
                "radnja1 (cafe)",
                "dinner (7,4) 90",
                "dinner2 (6,5) 180",
            };
            for (int i = 0; i < venues.Length; i++)
                if (!WalkObstacles.PhysicalVenueName(venues[i]))
                    failures.Add($"Walk props: streamed venue shell {venues[i]} was not physical.");

            var openLots = new[]
            {
                "caryard (4,5) 0", "kosarkaskiteren (3,4) 90", "park",
                "building-house", "building-office",
            };
            for (int i = 0; i < openLots.Length; i++)
                if (WalkObstacles.PhysicalVenueName(openLots[i]))
                    failures.Add($"Walk props: open amenity {openLots[i]} became a solid venue shell.");
        }

        static void AlignedStepsStayTogether(List<string> failures)
        {
            var steps = new[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0.08f, 0f, 1f),
                new Vector3(-0.05f, 0f, 1f),
            };
            float spread = CrewAudit.FormationHeadingSpread(steps, 0.025f);
            if (spread > 10f)
                failures.Add($"CrewAudit: aligned crew steps read as {spread:F1} degrees apart");
        }

        static void DivergentStepIsMeasured(List<string> failures)
        {
            var steps = new[] { Vector3.forward, Vector3.right, Vector3.forward };
            float spread = CrewAudit.FormationHeadingSpread(steps, 0.025f);
            if (Mathf.Abs(spread - 90f) > 0.01f)
                failures.Add($"CrewAudit: a right-angle split measured {spread:F1}, not 90 degrees");
        }

        static void StationaryJitterHasNoHeading(List<string> failures)
        {
            var steps = new[]
            {
                Vector3.forward,
                new Vector3(-0.001f, 0f, 0f),
            };
            float spread = CrewAudit.FormationHeadingSpread(steps, 0.025f);
            if (spread > 0.01f)
                failures.Add($"CrewAudit: stationary jitter invented a {spread:F1}-degree heading");
        }

        static void GroundSpreadIgnoresHeight(List<string> failures)
        {
            var positions = new[]
            {
                new Vector3(0f, -4f, 0f),
                new Vector3(6f, 9f, 8f),
                new Vector3(3f, 100f, 4f),
            };
            float spread = CrewAudit.FormationPositionSpread(positions);
            if (Mathf.Abs(spread - 10f) > 0.01f)
                failures.Add($"CrewAudit: 6-8-10 ground spread measured {spread:F2} m");
        }

        static void BreachNeedsItsWholeGrace(List<string> failures)
        {
            float held = 0f;
            if (CrewAudit.AdvanceSustained(ref held, true, 0.5f, 1.5f) ||
                CrewAudit.AdvanceSustained(ref held, true, 0.5f, 1.5f) ||
                CrewAudit.AdvanceSustained(ref held, true, 0.5f, 1.5f))
                failures.Add("CrewAudit: a breach faults before its full grace elapses");
            if (!CrewAudit.AdvanceSustained(ref held, true, 0.01f, 1.5f))
                failures.Add("CrewAudit: a sustained breach does not fault after its grace");
        }

        static void ClearingABreachResetsTheClock(List<string> failures)
        {
            float held = 1.4f;
            if (CrewAudit.AdvanceSustained(ref held, false, 0.1f, 1.5f) || held != 0f)
                failures.Add("CrewAudit: clearing a formation breach does not reset its clock");
            if (CrewAudit.AdvanceSustained(ref held, true, 0.2f, 1.5f))
                failures.Add("CrewAudit: a cleared breach resumed on its old clock");
        }
    }
}
