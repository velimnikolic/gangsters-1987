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
            CombatCornerHandoffDoesNotOrbit(failures);
            MovingWithoutProgressReplans(failures);
            RoutedCombatStrideIsASeparationMover(failures);
            TwoRoutedMoversDoNotCancelOneAnother(failures);
            RejectedRouteStartCanRecover(failures);
            RouteStallUsesRecentTravel(failures);
            RouteOrbitRequiresLostGround(failures);
            CombatRouteFailureReachesCoverCaller(failures);
            RoutedSteerMayStepSidewaysButNotBack(failures);
            NearRouteCornerIsRetained(failures);
            return failures;
        }

        static void NearRouteCornerIsRetained(List<string> failures)
        {
            if (!WalkRoute.RetainPulledCornerModel(0.005f * 0.005f) ||
                WalkRoute.RetainPulledCornerModel(0f))
                failures.Add("WalkRoute: a required near tangent was silently omitted.");
        }

        static void RouteOrbitRequiresLostGround(List<string> failures)
        {
            if (CrewAudit.RouteOrbitModel(30f, 332f, 29f) ||
                !CrewAudit.RouteOrbitModel(8f, 360f, 2f) ||
                !CrewAudit.RouteOrbitModel(3f, 20f, 0.5f) ||
                CrewAudit.RouteOrbitModel(2f, 360f, 0f))
                failures.Add("CrewAudit: straight pursuit is an orbit or a real loop is missed.");
        }

        static void RoutedSteerMayStepSidewaysButNotBack(List<string> failures)
        {
            float perpendicular = Mathf.Cos(90f * Mathf.Deg2Rad);
            float backwards = Mathf.Cos(110f * Mathf.Deg2Rad);
            if (!CrewWalker.RoutedHeadingAllowedModel(perpendicular) ||
                CrewWalker.RoutedHeadingAllowedModel(backwards))
                failures.Add("Combat route: tangent escape is blocked or reverse fallback is allowed.");
        }

        static void CombatRouteFailureReachesCoverCaller(List<string> failures)
        {
            if (CrewWalker.CombatRouteFailedModel(0.8f, false) ||
                !CrewWalker.CombatRouteFailedModel(0.81f, false) ||
                !CrewWalker.CombatRouteFailedModel(0f, true))
                failures.Add("Combat route: a failed cover approach is hidden by corner reset.");
        }

        static void RouteStallUsesRecentTravel(List<string> failures)
        {
            float elapsed = 0f, recent = 0f;
            if (CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.11f, 0.5f) ||
                CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0f, 0.5f) ||
                CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0f, 0.5f) ||
                !CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0f, 0.5f))
                failures.Add("CrewAudit: an early route nudge grants permanent stall immunity.");

            elapsed = 0f;
            recent = 0f;
            if (CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.04f, 0.5f) ||
                CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.04f, 0.5f) ||
                CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.04f, 0.5f) ||
                elapsed != 0f || recent != 0f)
                failures.Add("CrewAudit: real recent route movement does not renew stall grace.");

            elapsed = 0f;
            recent = 0f;
            if (CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.03f, 0.5f) ||
                CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.03f, 0.5f) ||
                !CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.03f, 0.5f))
                failures.Add("CrewAudit: sub-threshold shuffling hides a routed stall.");
        }

        static void RejectedRouteStartCanRecover(List<string> failures)
        {
            if (!WalkObstacles.RouteStartNeedsRecoveryModel(
                    clearanceBlocked: true, centreOverlapping: false,
                    hasValidator: false, validatorAccepts: true) ||
                !WalkObstacles.RouteStartNeedsRecoveryModel(
                    clearanceBlocked: false, centreOverlapping: false,
                    hasValidator: true, validatorAccepts: false) ||
                WalkObstacles.RouteStartNeedsRecoveryModel(
                    clearanceBlocked: false, centreOverlapping: false,
                    hasValidator: true, validatorAccepts: true) ||
                WalkObstacles.RouteStartNeedsRecoveryModel(
                    clearanceBlocked: true, centreOverlapping: true,
                    hasValidator: true, validatorAccepts: false))
                failures.Add("Combat route: a clear but lattice-isolated start cannot recover safely.");
        }

        static void TwoRoutedMoversDoNotCancelOneAnother(List<string> failures)
        {
            if (DemoCrews.SeparationPairNeedsEaseModel(true, true) ||
                !DemoCrews.SeparationPairNeedsEaseModel(true, false) ||
                !DemoCrews.SeparationPairNeedsEaseModel(false, true) ||
                !DemoCrews.SeparationPairNeedsEaseModel(false, false))
                failures.Add("Crew separation: two active routes can cancel each other's stride.");
        }

        static void RoutedCombatStrideIsASeparationMover(List<string> failures)
        {
            if (!DemoCrews.SeparationMoverModel(false, true) ||
                !DemoCrews.SeparationMoverModel(true, false) ||
                DemoCrews.SeparationMoverModel(false, false))
                failures.Add("Crew separation: routed combat movement was treated as a standing body.");
        }

        static void CombatCornerHandoffDoesNotOrbit(List<string> failures)
        {
            if (CrewWalker.CombatCornerCanAdvanceModel(0.001f, false) ||
                CrewWalker.CombatCornerCanAdvanceModel(0.02f, false) ||
                CrewWalker.CombatCornerCanAdvanceModel(0.2f, false) ||
                CrewWalker.CombatCornerCanAdvanceModel(0.8f, false) ||
                !CrewWalker.CombatCornerCanAdvanceModel(3f, true) ||
                !Mathf.Approximately(CrewWalker.CombatCornerStopModel(
                    last: false, endsAtTarget: false, terminalStop: 7f), 0f) ||
                !Mathf.Approximately(CrewWalker.CombatCornerStopModel(
                    last: true, endsAtTarget: true, terminalStop: 7f), 7f))
                failures.Add("Combat route: corner handoff ignored the proved next chord.");
        }

        static void MovingWithoutProgressReplans(List<string> failures)
        {
            float best = float.MaxValue, stalled = 0f;
            if (CrewWalker.CombatCornerStalledModel(5f, 0.5f, ref best, ref stalled) ||
                CrewWalker.CombatCornerStalledModel(4.7f, 0.5f, ref best, ref stalled))
                failures.Add("Combat route: real waypoint progress was read as a stall.");

            // Full movement with alternating 135-degree headings is still a stall when
            // none of it beats the closest point already reached.
            if (CrewWalker.CombatCornerStalledModel(5.2f, 0.5f, ref best, ref stalled) ||
                CrewWalker.CombatCornerStalledModel(4.9f, 0.5f, ref best, ref stalled) ||
                !CrewWalker.CombatCornerStalledModel(5.1f, 0.25f, ref best, ref stalled))
                failures.Add("Combat route: a moving orbit never forces a replan.");
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
